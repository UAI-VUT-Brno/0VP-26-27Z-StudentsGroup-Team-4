using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using UAI.ZeroVP.DTO;
using UAI.ZeroVP.Tests.Logging;

namespace UAI.ZeroVP.Tests;

/// <summary>HTTP-level tests for the overdue-todos query and its as-of parsing.</summary>
public sealed class OverdueTodosApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/todos";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;

    public OverdueTodosApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.RedirectLogsToTemp()).CreateClient();
    }

    [Fact]
    public async Task GetOverdue_WithOffsetAndUserId_ReturnsOnlyTasksOverdueAtTheUtcInstant()
    {
        var userId = $"overdue-{Guid.NewGuid():N}";
        var dueAtUtc = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var created = await _client.PostAsJsonAsync(
            $"{Url}/repeat",
            new CreateWeeklyTodoItemsRequest
            {
                Title = "Overdue candidate",
                UserId = userId,
                FirstDueAtUtc = dueAtUtc,
                RepeatCount = 1
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdItems = (await created.Content.ReadFromJsonAsync<List<TodoItemDto>>(JsonOptions))!;

        using var response = await _client.GetAsync(
            $"{Url}/overdue?asOfUtc=2099-01-01T02%3A00%3A00%2B01%3A00&userId={Uri.EscapeDataString(userId)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var overdueItems = (await response.Content.ReadFromJsonAsync<List<TodoItemDto>>(JsonOptions))!;
        var overdue = Assert.Single(overdueItems);
        Assert.Equal(createdItems[0].Id, overdue.Id);
        Assert.Equal(dueAtUtc, overdue.DueAtUtc);
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2099-01-01T00:00:00")]
    public async Task GetOverdue_InvalidOrTimezoneFreeAsOfUtc_Returns400ProblemDetails(string asOfUtc)
    {
        using var response = await _client.GetAsync($"{Url}/overdue?asOfUtc={Uri.EscapeDataString(asOfUtc)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("Invalid asOfUtc", problem.GetProperty("title").GetString());
    }
}
