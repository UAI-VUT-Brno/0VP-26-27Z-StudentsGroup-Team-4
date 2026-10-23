using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

/// <summary>
/// HTTP-level tests of POST /api/todos/repeat on the real application (in-memory test server).
/// They cover what unit tests cannot: JSON binding, the [ApiController] validation response
/// and the guarantee that an invalid request creates no tasks.
/// </summary>
public class WeeklyTodosApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/todos/repeat";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;

    public WeeklyTodosApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    // The application starts with sample data, so tests compare counts before and after.
    private async Task<int> CountTodosAsync() =>
        (await _client.GetFromJsonAsync<List<TodoItemDto>>("/api/todos", JsonOptions))!.Count;

    [Fact]
    public async Task Repeat_ValidRequest_Returns201WithAllOccurrences()
    {
        var before = await CountTodosAsync();
        const string body = """
            {"title":"Weekly report","description":"Send it","priority":"High",
             "firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":3}
            """;

        using var response = await _client.PostAsync(Url, Json(body));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"dueAtUtc\"", raw); // the contract property name

        var created = JsonSerializer.Deserialize<List<TodoItemDto>>(raw, JsonOptions)!;
        var first = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(
            new DateTimeOffset?[] { first, first.AddDays(7), first.AddDays(14), first.AddDays(21) },
            created.Select(t => t.DueAtUtc));
        Assert.Equal(4, created.Select(t => t.Id).Distinct().Count());
        Assert.All(created, task =>
        {
            Assert.Equal("Weekly report", task.Title);
            Assert.Equal("Send it", task.Description);
            Assert.Equal(TodoPriority.High, task.Priority);
            Assert.False(task.IsCompleted);
        });
        Assert.Equal(before + 4, await CountTodosAsync());
    }

    [Fact]
    public async Task Repeat_EveryCreatedOccurrenceCanBeReadById()
    {
        const string body = """{"title":"Readable","firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":2}""";
        using var response = await _client.PostAsync(Url, Json(body));
        var created = (await response.Content.ReadFromJsonAsync<List<TodoItemDto>>(JsonOptions))!;

        foreach (var task in created)
        {
            var fetched = await _client.GetFromJsonAsync<TodoItemDto>($"/api/todos/{task.Id}", JsonOptions);

            Assert.Equal(task.DueAtUtc, fetched!.DueAtUtc);
        }
    }

    [Fact]
    public async Task Repeat_OccurrencesAreIndependent_DeletingOneKeepsTheOthers()
    {
        const string body = """{"title":"Independent","firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":2}""";
        using var response = await _client.PostAsync(Url, Json(body));
        var created = (await response.Content.ReadFromJsonAsync<List<TodoItemDto>>(JsonOptions))!;

        using var deleted = await _client.DeleteAsync($"/api/todos/{created[0].Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var second = await _client.GetAsync($"/api/todos/{created[1].Id}");
        using var third = await _client.GetAsync($"/api/todos/{created[2].Id}");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
    }

    [Theory]
    [InlineData("""{"title":"","firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":3}""")]
    [InlineData("""{"firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":3}""")]
    [InlineData("""{"title":"A","priority":"Urgent","firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":3}""")]
    [InlineData("""{"title":"A","repeatCount":3}""")]
    [InlineData("""{"title":"A","firstDueAtUtc":null,"repeatCount":3}""")]
    [InlineData("""{"title":"A","firstDueAtUtc":"not-a-date","repeatCount":3}""")]
    [InlineData("""{"title":"A","firstDueAtUtc":"2026-11-02T09:00:00Z"}""")]
    [InlineData("""{"title":"A","firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":0}""")]
    [InlineData("""{"title":"A","firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":-1}""")]
    [InlineData("""{"title":"A","firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":53}""")]
    [InlineData("""{"title":"A","firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":1.5}""")]
    [InlineData("""{"title":"A","firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":"three"}""")]
    [InlineData("""{"title":"A","firstDueAtUtc":"9999-12-31T00:00:00Z","repeatCount":52}""")]
    [InlineData("{")]
    [InlineData("")]
    public async Task Repeat_InvalidRequest_Returns400ValidationResponseAndCreatesNoTasks(string body)
    {
        var before = await CountTodosAsync();

        using var response = await _client.PostAsync(Url, Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("errors", out var errors));
        Assert.NotEmpty(errors.EnumerateObject());

        Assert.Equal(before, await CountTodosAsync());
    }

    [Fact]
    public async Task Repeat_InvalidRepeatCount_ReportsTheRepeatCountField()
    {
        const string body = """{"title":"A","firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":53}""";

        using var response = await _client.PostAsync(Url, Json(body));

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var fields = problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name);
        Assert.Contains(fields, name => name.Equals("RepeatCount", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_RegularTaskWithoutDueDate_StillWorks()
    {
        const string body = """{"title":"Plain task","priority":"Low"}""";

        using var response = await _client.PostAsync("/api/todos", Json(body));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TodoItemDto>(JsonOptions);
        Assert.Null(created!.DueAtUtc);
    }
}
