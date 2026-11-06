using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using UAI.ZeroVP.Tests.Logging;

namespace UAI.ZeroVP.Tests;

/// <summary>A task created through the REST API before the MCP tests run, with what the tests expect to read back.</summary>
public sealed record SeededTask(Guid Id, string Title, string UserId, bool IsCompleted);

/// <summary>
/// Starts the real application as an OS process on a local port (port 0 = a free port the OS picks)
/// with the MCP endpoint switched on, and fills it with a data set of <see cref="TaskCount"/> tasks
/// through the REST API. The process and the data are shared by all tests of <see cref="McpServerIntegrationTests"/>.
/// </summary>
public sealed class McpServerFixture : IAsyncLifetime
{
    /// <summary>Number of tasks in the data set: enough to see that the tools handle a realistic list, not just a handful.</summary>
    public const int TaskCount = 120;

    private static readonly string[] Users = ["anna", "petr", "eva"];
    private static readonly string[] Priorities = ["Low", "Normal", "High"];

    private readonly TempDir _temp = new();
    private readonly ConcurrentBag<ApiProcess> _processes = new();

    /// <summary>Base address of the running API, for example <c>http://127.0.0.1:54321</c>.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>The tasks that exist in the running API.</summary>
    public IReadOnlyList<SeededTask> Tasks { get; private set; } = Array.Empty<SeededTask>();

    public async Task InitializeAsync()
    {
        var dataFile = Path.Combine(_temp.FullPath, "shared", "todos.json");
        var logDirectory = Path.Combine(_temp.FullPath, "logs");

        var server = await ApiProcess.StartAsync(
            "mcp-tests",
            dataFile,
            logDirectory,
            _processes,
            new Dictionary<string, string> { ["Mcp__Enabled"] = "true" });

        BaseUrl = server.BaseUrl;

        using var http = server.CreateClient();
        var seeded = new List<SeededTask>(TaskCount);

        for (var i = 0; i < TaskCount; i++)
        {
            var title = $"Task {i:000}";
            var userId = Users[i % Users.Length];
            var isCompleted = i % 4 == 0;

            using var created = await http.PostAsJsonAsync("/api/todos", new
            {
                title,
                description = $"Sample task number {i}.",
                priority = Priorities[(i / 2) % Priorities.Length],
                userId
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            if (isCompleted)
            {
                using var completed = await http.PostAsync($"/api/todos/{id}/complete", content: null);
                Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            }

            seeded.Add(new SeededTask(id, title, userId, isCompleted));
        }

        Tasks = seeded;
    }

    public async Task DisposeAsync()
    {
        foreach (var process in _processes)
        {
            await process.DisposeAsync();
        }

        _temp.Dispose();
    }
}

/// <summary>
/// The MCP server tested the way a real client uses it: the API runs as a separate process on a local
/// port and the tests talk to it with the official MCP client over HTTP. They list the tools and call
/// each of them, so the whole MCP protocol is exercised - not just the fact that the URL responds.
/// </summary>
[Trait("Category", "MultiProcess")]
[Trait("Category", "Mcp")]
public sealed class McpServerIntegrationTests : IClassFixture<McpServerFixture>
{
    private const int TestTimeoutMilliseconds = 60_000;

    private readonly McpServerFixture _fixture;

    public McpServerIntegrationTests(McpServerFixture fixture) => _fixture = fixture;

    private static int PriorityRank(string priority) => priority switch
    {
        "Low" => 0,
        "Normal" => 1,
        "High" => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unexpected priority.")
    };

    private async Task<McpClient> ConnectAsync()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(new Uri(_fixture.BaseUrl), "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        });

        return await McpClient.CreateAsync(transport);
    }

    private static string TextOf(CallToolResult result)
    {
        var block = Assert.Single(result.Content);

        return Assert.IsType<TextContentBlock>(block).Text;
    }

    private static async Task<JsonDocument> ListTodosAsync(McpClient client, bool? isCompleted = null, string? userId = null)
    {
        var arguments = new Dictionary<string, object?>();

        if (isCompleted is not null)
        {
            arguments["isCompleted"] = isCompleted.Value;
        }

        if (userId is not null)
        {
            arguments["userId"] = userId;
        }

        var result = await client.CallToolAsync("list_todos", arguments);

        Assert.NotEqual(true, result.IsError);

        return JsonDocument.Parse(TextOf(result));
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
    public async Task ListTools_ReturnsOnlyTheTwoReadOnlyTools()
    {
        await using var client = await ConnectAsync();

        var tools = await client.ListToolsAsync();

        Assert.Equal(
            new[] { "get_todo", "list_todos" },
            tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(tools, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
        });
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
    public async Task ListTodos_WithoutArguments_ReturnsEveryTaskOrderedByPriority()
    {
        await using var client = await ConnectAsync();

        using var document = await ListTodosAsync(client);
        var items = document.RootElement.EnumerateArray().ToList();

        Assert.Equal(McpServerFixture.TaskCount, items.Count);
        Assert.Equal(
            _fixture.Tasks.Select(task => task.Id).Order(),
            items.Select(item => item.GetProperty("id").GetGuid()).Order());

        var ranks = items.Select(item => PriorityRank(item.GetProperty("priority").GetString()!)).ToList();
        Assert.Equal(ranks.OrderByDescending(rank => rank), ranks);
    }

    [Theory(Timeout = TestTimeoutMilliseconds)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListTodos_FilteredByCompletion_ReturnsOnlyMatchingTasks(bool isCompleted)
    {
        await using var client = await ConnectAsync();
        var expected = _fixture.Tasks.Where(task => task.IsCompleted == isCompleted).Select(task => task.Id).Order().ToList();

        using var document = await ListTodosAsync(client, isCompleted: isCompleted);
        var items = document.RootElement.EnumerateArray().ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, items.Select(item => item.GetProperty("id").GetGuid()).Order());
        Assert.All(items, item => Assert.Equal(isCompleted, item.GetProperty("isCompleted").GetBoolean()));
    }

    [Theory(Timeout = TestTimeoutMilliseconds)]
    [InlineData("anna")]
    [InlineData("ANNA")]
    [InlineData("petr")]
    [InlineData("eva")]
    public async Task ListTodos_FilteredByUser_ReturnsOnlyThatUsersTasks(string userId)
    {
        await using var client = await ConnectAsync();
        var expected = _fixture.Tasks
            .Where(task => string.Equals(task.UserId, userId, StringComparison.OrdinalIgnoreCase))
            .Select(task => task.Id)
            .Order()
            .ToList();

        using var document = await ListTodosAsync(client, userId: userId);
        var items = document.RootElement.EnumerateArray().ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, items.Select(item => item.GetProperty("id").GetGuid()).Order());
        Assert.All(items, item => Assert.Equal(userId, item.GetProperty("userId").GetString(), ignoreCase: true));
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
    public async Task ListTodos_FilteredByUserAndCompletion_CombinesBothFilters()
    {
        await using var client = await ConnectAsync();
        var expected = _fixture.Tasks
            .Where(task => task.UserId == "anna" && !task.IsCompleted)
            .Select(task => task.Id)
            .Order()
            .ToList();

        using var document = await ListTodosAsync(client, isCompleted: false, userId: "anna");
        var items = document.RootElement.EnumerateArray().ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, items.Select(item => item.GetProperty("id").GetGuid()).Order());
        Assert.All(items, item =>
        {
            Assert.Equal("anna", item.GetProperty("userId").GetString());
            Assert.False(item.GetProperty("isCompleted").GetBoolean());
        });
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
    public async Task ListTodos_ForUnknownUser_ReturnsEmptyArray()
    {
        await using var client = await ConnectAsync();

        using var document = await ListTodosAsync(client, userId: "nobody");

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    [Theory(Timeout = TestTimeoutMilliseconds)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetTodo_ReturnsTheRequestedTask(bool completed)
    {
        await using var client = await ConnectAsync();
        var expected = _fixture.Tasks.First(task => task.IsCompleted == completed);

        var result = await client.CallToolAsync("get_todo", new Dictionary<string, object?> { ["id"] = expected.Id.ToString() });

        Assert.NotEqual(true, result.IsError);

        using var document = JsonDocument.Parse(TextOf(result));
        var task = document.RootElement;

        Assert.Equal(expected.Id, task.GetProperty("id").GetGuid());
        Assert.Equal(expected.Title, task.GetProperty("title").GetString());
        Assert.Equal(expected.UserId, task.GetProperty("userId").GetString());
        Assert.Equal(expected.IsCompleted, task.GetProperty("isCompleted").GetBoolean());
        Assert.Contains(task.GetProperty("priority").GetString(), new[] { "Low", "Normal", "High" });
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
    public async Task GetTodo_ReturnsTheSameTaskAsTheRestApi()
    {
        await using var client = await ConnectAsync();
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };
        var expected = _fixture.Tasks[^1];

        var rest = await http.GetFromJsonAsync<JsonElement>($"/api/todos/{expected.Id}");
        var result = await client.CallToolAsync("get_todo", new Dictionary<string, object?> { ["id"] = expected.Id.ToString() });
        using var mcp = JsonDocument.Parse(TextOf(result));

        foreach (var property in new[] { "id", "title", "description", "priority", "isCompleted", "createdAtUtc", "userId" })
        {
            Assert.Equal(rest.GetProperty(property).ToString(), mcp.RootElement.GetProperty(property).ToString());
        }
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
    public async Task GetTodo_WithUnknownId_ReturnsToolError()
    {
        await using var client = await ConnectAsync();
        var unknownId = Guid.NewGuid().ToString();

        var result = await client.CallToolAsync("get_todo", new Dictionary<string, object?> { ["id"] = unknownId });

        Assert.True(result.IsError);
        Assert.Contains(unknownId, TextOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
    public async Task GetTodo_WithInvalidId_ReturnsToolError()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("get_todo", new Dictionary<string, object?> { ["id"] = "not-a-guid" });

        Assert.True(result.IsError);
        Assert.Contains("not-a-guid", TextOf(result), StringComparison.Ordinal);
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
    public async Task CallingTools_DoesNotChangeTheData()
    {
        await using var client = await ConnectAsync();
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };

        using var all = await ListTodosAsync(client);
        await client.CallToolAsync("get_todo", new Dictionary<string, object?> { ["id"] = _fixture.Tasks[0].Id.ToString() });

        var restList = await http.GetFromJsonAsync<JsonElement>("/api/todos");

        Assert.Equal(McpServerFixture.TaskCount, restList.GetArrayLength());
    }
}

/// <summary>
/// Without an explicit opt-in the MCP endpoint must not exist: the application behaves like before
/// (<c>Mcp:Enabled</c> is <c>false</c> in <c>appsettings.json</c>) and the REST API keeps working.
/// </summary>
[Trait("Category", "MultiProcess")]
[Trait("Category", "Mcp")]
public sealed class McpDisabledByDefaultTests : IAsyncLifetime
{
    private readonly TempDir _temp = new();
    private readonly ConcurrentBag<ApiProcess> _processes = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var process in _processes)
        {
            await process.DisposeAsync();
        }

        _temp.Dispose();
    }

    [Fact(Timeout = 60_000)]
    public async Task McpEndpoint_IsNotServed_UnlessEnabled()
    {
        var server = await ApiProcess.StartAsync(
            "mcp-disabled",
            Path.Combine(_temp.FullPath, "shared", "todos.json"),
            Path.Combine(_temp.FullPath, "logs"),
            _processes);
        using var client = server.CreateClient();

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var mcp = await client.PostAsync("/mcp", content);
        using var rest = await client.GetAsync("/api/todos");

        Assert.Equal(HttpStatusCode.NotFound, mcp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rest.StatusCode);
    }
}
