using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

/// <summary>
/// HTTP-level tests of GET /api/todos?userId=... on the real application (in-memory test server).
/// They verify the "My Tasks" view end to end: query binding, filtering, combination with
/// the isCompleted filter and data isolation between users.
/// </summary>
public class TodosUserFilterApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/todos";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;

    public TodosUserFilterApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // Every test uses its own unique user ids, so tests cannot affect each other
    // even though the application keeps its data in memory and starts with sample data.
    private static string NewUserId(string name) => $"{name}-{Guid.NewGuid():N}";

    private async Task<TodoItemDto> CreateTaskAsync(string title, string? userId, bool complete = false)
    {
        using var created = await _client.PostAsJsonAsync(Url, new { title, userId }, JsonOptions);
        created.EnsureSuccessStatusCode();
        var task = (await created.Content.ReadFromJsonAsync<TodoItemDto>(JsonOptions))!;

        if (!complete)
        {
            return task;
        }

        using var completed = await _client.PostAsync($"{Url}/{task.Id}/complete", content: null);
        completed.EnsureSuccessStatusCode();
        return (await completed.Content.ReadFromJsonAsync<TodoItemDto>(JsonOptions))!;
    }

    private async Task<List<TodoItemDto>> GetTasksAsync(string query)
    {
        using var response = await _client.GetAsync($"{Url}{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<TodoItemDto>>(JsonOptions))!;
    }

    [Fact]
    public async Task GetAll_WithUserId_Returns200WithOnlyThatUsersTasks()
    {
        var anna = NewUserId("anna");
        var petr = NewUserId("petr");
        var annaTask1 = await CreateTaskAsync("Anna 1", anna);
        var annaTask2 = await CreateTaskAsync("Anna 2", anna);
        await CreateTaskAsync("Petr 1", petr);

        var result = await GetTasksAsync($"?userId={anna}");

        Assert.Equal(2, result.Count);
        Assert.All(result, task => Assert.Equal(anna, task.UserId));
        Assert.Equivalent(new[] { annaTask1.Id, annaTask2.Id }, result.Select(t => t.Id));
    }

    [Fact]
    public async Task GetAll_WithUserId_DoesNotLeakTasksOfOtherUsers()
    {
        var userA = NewUserId("a");
        var userB = NewUserId("b");
        var userC = NewUserId("c");
        await CreateTaskAsync("A task", userA);
        var foreign = new List<TodoItemDto>
        {
            await CreateTaskAsync("B task 1", userB),
            await CreateTaskAsync("B task 2", userB, complete: true),
            await CreateTaskAsync("C task 1", userC),
            await CreateTaskAsync("Task without owner", userId: null)
        };

        var result = await GetTasksAsync($"?userId={userA}");

        var item = Assert.Single(result);
        Assert.Equal(userA, item.UserId);
        var returnedIds = result.Select(t => t.Id).ToHashSet();
        Assert.DoesNotContain(foreign, task => returnedIds.Contains(task.Id));
    }

    [Fact]
    public async Task GetAll_WithUserId_IgnoresCaseOfTheFilter()
    {
        var anna = NewUserId("anna");
        var task = await CreateTaskAsync("Anna", anna);

        var result = await GetTasksAsync($"?userId={anna.ToUpperInvariant()}");

        var item = Assert.Single(result);
        Assert.Equal(task.Id, item.Id);
    }

    [Fact]
    public async Task GetAll_UnknownUserId_Returns200WithEmptyList()
    {
        await CreateTaskAsync("Someone's task", NewUserId("anna"));

        var result = await GetTasksAsync($"?userId={NewUserId("nobody")}");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAll_EmptyUserId_Returns200WithEmptyList()
    {
        await CreateTaskAsync("Someone's task", NewUserId("anna"));
        await CreateTaskAsync("Task without owner", userId: null);

        var result = await GetTasksAsync("?userId=");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAll_UserIdAndIsCompletedFalse_ReturnsOnlyOpenTasksOfThatUser()
    {
        var anna = NewUserId("anna");
        var petr = NewUserId("petr");
        await CreateTaskAsync("Anna done", anna, complete: true);
        var open = await CreateTaskAsync("Anna open", anna);
        await CreateTaskAsync("Petr open", petr);

        var result = await GetTasksAsync($"?userId={anna}&isCompleted=false");

        var item = Assert.Single(result);
        Assert.Equal(open.Id, item.Id);
        Assert.False(item.IsCompleted);
        Assert.Equal(anna, item.UserId);
    }

    [Fact]
    public async Task GetAll_UserIdAndIsCompletedTrue_ReturnsOnlyCompletedTasksOfThatUser()
    {
        var anna = NewUserId("anna");
        var petr = NewUserId("petr");
        var done = await CreateTaskAsync("Anna done", anna, complete: true);
        await CreateTaskAsync("Anna open", anna);
        await CreateTaskAsync("Petr done", petr, complete: true);

        var result = await GetTasksAsync($"?userId={anna}&isCompleted=true");

        var item = Assert.Single(result);
        Assert.Equal(done.Id, item.Id);
        Assert.True(item.IsCompleted);
        Assert.Equal(anna, item.UserId);
    }

    [Fact]
    public async Task GetAll_WithoutUserId_Returns200WithTasksOfAllUsers()
    {
        var anna = await CreateTaskAsync("Anna", NewUserId("anna"));
        var petr = await CreateTaskAsync("Petr", NewUserId("petr"));
        var nobody = await CreateTaskAsync("Nobody", userId: null);

        var result = await GetTasksAsync(string.Empty);

        var ids = result.Select(t => t.Id).ToHashSet();
        Assert.Contains(anna.Id, ids);
        Assert.Contains(petr.Id, ids);
        Assert.Contains(nobody.Id, ids);
    }
}