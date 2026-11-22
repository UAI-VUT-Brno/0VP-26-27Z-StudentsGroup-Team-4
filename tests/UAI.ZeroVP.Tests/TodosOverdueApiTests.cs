using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.DTO;
using UAI.ZeroVP.Tests.Logging;

namespace UAI.ZeroVP.Tests;

public sealed class TodosOverdueApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/todos/overdue";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly ITodoRepository _repository;

    public TodosOverdueApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.RedirectLogsToTemp());
        _client = _factory.CreateClient();
        _repository = _factory.Services.GetRequiredService<ITodoRepository>();
    }

    [Fact]
    public async Task GetOverdue_FiltersAndOrdersTasks_AndSupportsUserIdAndReferenceTime()
    {
        var userId = $"overdue-{Guid.NewGuid():N}";
        var asOfUtc = new DateTimeOffset(2026, 1, 16, 12, 0, 0, TimeSpan.Zero);
        var earlier = await AddTaskAsync("Earlier", userId, asOfUtc.AddSeconds(-2));
        var highPriority = await AddTaskAsync("High priority tie", userId, asOfUtc.AddSeconds(-1), TodoPriority.High);
        var lowPriority = await AddTaskAsync("Low priority tie", userId, asOfUtc.AddSeconds(-1), TodoPriority.Low);
        await AddTaskAsync("Exactly due", userId, asOfUtc);
        await AddTaskAsync("Completed", userId, asOfUtc.AddDays(-1), isCompleted: true);
        await AddTaskAsync("No due date", userId, null);
        await AddTaskAsync("Another user", $"other-{Guid.NewGuid():N}", asOfUtc.AddDays(-1));

        var query = $"?asOfUtc={Uri.EscapeDataString(asOfUtc.ToString("O", CultureInfo.InvariantCulture))}&userId={userId}";
        using var response = await _client.GetAsync($"{Url}{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<List<TodoItemDto>>(JsonOptions))!;
        Assert.Equal(new[] { earlier.Id, highPriority.Id, lowPriority.Id }, result.Select(item => item.Id));
    }

    [Fact]
    public async Task GetOverdue_PaddedUserId_IsTrimmedAndReturnsThatUsersTasks()
    {
        var userId = $"overdue-{Guid.NewGuid():N}";
        var asOfUtc = new DateTimeOffset(2026, 1, 16, 12, 0, 0, TimeSpan.Zero);
        var overdue = await AddTaskAsync("Overdue", userId, asOfUtc.AddSeconds(-1));
        await AddTaskAsync("Another user", $"other-{Guid.NewGuid():N}", asOfUtc.AddSeconds(-1));

        var query = $"?asOfUtc={Uri.EscapeDataString(asOfUtc.ToString("O", CultureInfo.InvariantCulture))}&userId=%20{userId}%20";
        using var response = await _client.GetAsync($"{Url}{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<List<TodoItemDto>>(JsonOptions))!;
        var item = Assert.Single(result);
        Assert.Equal(overdue.Id, item.Id);
        Assert.Equal(userId, item.UserId);
    }

    [Fact]
    public async Task GetOverdue_WhitespaceOnlyUserId_Returns200WithEmptyList()
    {
        var userId = $"overdue-{Guid.NewGuid():N}";
        var asOfUtc = new DateTimeOffset(2026, 1, 16, 12, 0, 0, TimeSpan.Zero);
        await AddTaskAsync("Overdue", userId, asOfUtc.AddSeconds(-1));

        var query = $"?asOfUtc={Uri.EscapeDataString(asOfUtc.ToString("O", CultureInfo.InvariantCulture))}&userId=%20%20";
        using var response = await _client.GetAsync($"{Url}{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<List<TodoItemDto>>(JsonOptions))!;
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOverdue_InvalidAsOfUtc_ReturnsProblemDetails()
    {
        using var response = await _client.GetAsync($"{Url}?asOfUtc=not-a-date");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    }

    [Fact]
    public async Task GetOverdue_AsOfUtcWithoutOffset_ReturnsValidationProblemDetails()
    {
        // "2026-11-18T10:00:00" is a valid ISO 8601 date and time, but without an explicit
        // offset it would otherwise be silently interpreted in the server's local timezone.
        using var response = await _client.GetAsync($"{Url}?asOfUtc=2026-11-18T10:00:00");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Contains("asOfUtc", problem.Errors.Keys);
    }

    [Fact]
    public async Task GetOverdue_EmptyAsOfUtc_ReturnsValidationProblemDetails()
    {
        using var response = await _client.GetAsync($"{Url}?asOfUtc=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Contains("asOfUtc", problem.Errors.Keys);
    }

    [Theory]
    [InlineData("2026-11-18T10:00:00Z%0A")]
    [InlineData("2026-11-18T10:00:00+02:00%0A")]
    public async Task GetOverdue_AsOfUtcWithTrailingNewline_ReturnsValidationProblemDetails(string rawAsOfUtc)
    {
        // The value is sent pre-encoded: "$" in the validation regex would accept a trailing "\n", "\z" must not.
        using var response = await _client.GetAsync($"{Url}?asOfUtc={rawAsOfUtc}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("asOfUtc", problem.Errors.Keys);
    }

    [Fact]
    public async Task GetOverdue_AsOfUtcWithUnescapedPlus_ReturnsValidationProblemDetails()
    {
        // An unescaped "+" is decoded as a space in a query string, so the offset is lost.
        using var response = await _client.GetAsync($"{Url}?asOfUtc=2026-11-18T10:00:00+02:00");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("asOfUtc", problem.Errors.Keys);
    }

    [Fact]
    public async Task GetOverdue_EmptyUserId_Returns200WithEmptyList()
    {
        var userId = $"overdue-{Guid.NewGuid():N}";
        await AddTaskAsync("Someone's task", userId, DateTimeOffset.UtcNow.AddDays(-1));
        await AddTaskAsync("Task without owner", null, DateTimeOffset.UtcNow.AddDays(-1));

        using var response = await _client.GetAsync($"{Url}?userId=");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<List<TodoItemDto>>(JsonOptions))!;
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("2026-11-18T10:00:00Z")]
    [InlineData("2026-11-18T10:00:00+00:00")]
    [InlineData("2026-11-18T12:00:00+02:00")]
    public async Task GetOverdue_AsOfUtcWithExplicitOffset_ReturnsOk(string asOfUtc)
    {
        using var response = await _client.GetAsync($"{Url}?asOfUtc={Uri.EscapeDataString(asOfUtc)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<TodoItem> AddTaskAsync(
        string title,
        string? userId,
        DateTimeOffset? dueAtUtc,
        TodoPriority priority = TodoPriority.Normal,
        bool isCompleted = false) =>
        _repository.AddAsync(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = title,
            Priority = priority,
            IsCompleted = isCompleted,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = isCompleted ? DateTimeOffset.UtcNow : null,
            DueAtUtc = dueAtUtc,
            UserId = userId
        });
}
