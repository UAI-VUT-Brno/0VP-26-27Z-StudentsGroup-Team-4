using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.API.Controllers;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

/// <summary>
/// The weekly endpoint called as a normal class (no HTTP server), like the other controller tests.
/// Validation attributes are not evaluated here - see <see cref="WeeklyTodosApiTests"/> for that.
/// </summary>
public class TodosControllerWeeklyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 10, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstDue = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTodoRepository _repository =
        new(new InMemoryTodoOptions { SeedSampleData = false });

    private readonly CapturingLogger<TodosController> _logger = new();

    private TodosController CreateController() => new(_repository, new FixedTimeProvider(Now), _logger);

    private static CreateWeeklyTodoItemsRequest CreateRequest(int repeatCount = 3) => new()
    {
        Title = "  Weekly report  ",
        Description = "Send it to the team.",
        Priority = TodoPriority.High,
        FirstDueAtUtc = FirstDue,
        RepeatCount = repeatCount
    };

    private async Task<List<TodoItemDto>> CreateWeeklyAsync(int repeatCount = 3)
    {
        var result = await CreateController().CreateWeekly(CreateRequest(repeatCount), CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);
        return Assert.IsAssignableFrom<IReadOnlyList<TodoItemDto>>(response.Value).ToList();
    }

    [Fact]
    public async Task CreateWeekly_ValidRequest_Returns201()
    {
        var result = await CreateController().CreateWeekly(CreateRequest(), CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, response.StatusCode);
    }

    [Fact]
    public async Task CreateWeekly_ReturnsFirstOccurrencePlusRepeatCountInChronologicalOrder()
    {
        var created = await CreateWeeklyAsync(repeatCount: 3);

        Assert.Equal(
            new DateTimeOffset?[] { FirstDue, FirstDue.AddDays(7), FirstDue.AddDays(14), FirstDue.AddDays(21) },
            created.Select(t => t.DueAtUtc));
    }

    [Fact]
    public async Task CreateWeekly_EveryOccurrenceHasOwnIdAndCopiedFieldsAndStartsIncomplete()
    {
        var created = await CreateWeeklyAsync();

        Assert.Equal(created.Count, created.Select(t => t.Id).Distinct().Count());
        Assert.All(created, task =>
        {
            Assert.Equal("Weekly report", task.Title);
            Assert.Equal("Send it to the team.", task.Description);
            Assert.Equal(TodoPriority.High, task.Priority);
            Assert.False(task.IsCompleted);
            Assert.Null(task.CompletedAtUtc);
            Assert.Equal(Now, task.CreatedAtUtc);
        });
    }

    [Fact]
    public async Task CreateWeekly_PersistsEveryOccurrenceInTheRepository()
    {
        var created = await CreateWeeklyAsync(repeatCount: 3);

        Assert.Equal(4, await _repository.CountAsync());
        foreach (var task in created)
        {
            var stored = await _repository.GetByIdAsync(task.Id);
            Assert.NotNull(stored);
            Assert.Equal(task.DueAtUtc, stored!.DueAtUtc);
        }
    }

    [Fact]
    public async Task CreateWeekly_OccurrencesAreIndependent_CompletingOneDoesNotChangeTheOthers()
    {
        var created = await CreateWeeklyAsync(repeatCount: 2);

        await CreateController().Complete(created[1].Id, CancellationToken.None);

        var all = await _repository.GetAllAsync();
        Assert.Equal(1, all.Count(t => t.IsCompleted));
        Assert.Equal(created[1].Id, all.Single(t => t.IsCompleted).Id);
    }

    [Fact]
    public async Task CreateWeekly_OccurrencesAreIndependent_DeletingOneDoesNotChangeTheOthers()
    {
        var created = await CreateWeeklyAsync(repeatCount: 2);

        await CreateController().Delete(created[0].Id, CancellationToken.None);

        var remaining = await _repository.GetAllAsync();
        Assert.Equal(new[] { created[1].Id, created[2].Id }, remaining.Select(t => t.Id));
    }

    [Fact]
    public async Task CreateWeekly_WritesOneLogEntryWithTheWeeklyEventId()
    {
        await CreateWeeklyAsync(repeatCount: 3);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(3002, entry.EventId.Id);
        Assert.Equal("WeeklyTodosCreated", entry.EventId.Name);
        Assert.Contains("Created 4 weekly occurrences", entry.Message);
        Assert.Contains("2026-11-02T09:00:00Z", entry.Message);
        Assert.Contains("2026-11-23T09:00:00Z", entry.Message);
    }

    [Fact]
    public async Task Create_RegularTask_StillHasNoDueDate()
    {
        var result = await CreateController().Create(new CreateTodoItemRequest { Title = "Plain" }, CancellationToken.None);

        var created = Assert.IsType<CreatedAtRouteResult>(result.Result);
        var dto = Assert.IsType<TodoItemDto>(created.Value);
        Assert.Null(dto.DueAtUtc);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}
