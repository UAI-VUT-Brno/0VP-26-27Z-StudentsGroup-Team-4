using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class InMemoryTodoRepositoryQueryTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static InMemoryTodoRepository CreateEmptyRepository() =>
        new(new InMemoryTodoOptions { SeedSampleData = false });

    private static TodoItem CreateItem(
        string title,
        TodoPriority priority = TodoPriority.Normal,
        bool isCompleted = false,
        int hoursAfterStart = 0) => new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Priority = priority,
            IsCompleted = isCompleted,
            CreatedAtUtc = Created.AddHours(hoursAfterStart)
        };

    [Fact]
    public async Task GetAllAsync_SortsByPriorityDescendingThenByCreatedTime()
    {
        var repository = CreateEmptyRepository();
        await repository.AddAsync(CreateItem("Low", TodoPriority.Low));
        await repository.AddAsync(CreateItem("High later", TodoPriority.High, hoursAfterStart: 2));
        await repository.AddAsync(CreateItem("Normal", TodoPriority.Normal));
        await repository.AddAsync(CreateItem("High earlier", TodoPriority.High, hoursAfterStart: 1));

        var titles = (await repository.GetAllAsync()).Select(i => i.Title).ToArray();

        Assert.Equal(new[] { "High earlier", "High later", "Normal", "Low" }, titles);
    }

    [Fact]
    public async Task GetAllAsync_FiltersByCompletion()
    {
        var repository = CreateEmptyRepository();
        await repository.AddAsync(CreateItem("Done", isCompleted: true));
        await repository.AddAsync(CreateItem("Open 1"));
        await repository.AddAsync(CreateItem("Open 2"));

        var completed = await repository.GetAllAsync(isCompleted: true);
        var open = await repository.GetAllAsync(isCompleted: false);
        var all = await repository.GetAllAsync();

        Assert.Single(completed);
        Assert.Equal(2, open.Count);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task GetOverdueAsync_FiltersByAsOfCompletionAndUser_ThenSortsByDueDateAndPriority()
    {
        var repository = CreateEmptyRepository();
        var asOfUtc = Created.AddDays(10);
        var laterDue = CreateItem("Later high", TodoPriority.High);
        laterDue.UserId = "anna";
        laterDue.DueAtUtc = asOfUtc.AddDays(-1);
        var earlierLow = CreateItem("Earlier low", TodoPriority.Low);
        earlierLow.UserId = "ANNA";
        earlierLow.DueAtUtc = asOfUtc.AddDays(-2);
        var earlierHigh = CreateItem("Earlier high", TodoPriority.High);
        earlierHigh.UserId = "anna";
        earlierHigh.DueAtUtc = asOfUtc.AddDays(-2);
        var dueAtAsOf = CreateItem("Due now");
        dueAtAsOf.UserId = "anna";
        dueAtAsOf.DueAtUtc = asOfUtc;
        var completed = CreateItem("Completed", isCompleted: true);
        completed.UserId = "anna";
        completed.DueAtUtc = asOfUtc.AddDays(-3);
        var anotherUser = CreateItem("Other user");
        anotherUser.UserId = "petr";
        anotherUser.DueAtUtc = asOfUtc.AddDays(-4);
        var noDueDate = CreateItem("No due date");
        noDueDate.UserId = "anna";

        await repository.AddRangeAsync([laterDue, earlierLow, earlierHigh, dueAtAsOf, completed, anotherUser, noDueDate]);

        var overdue = await repository.GetOverdueAsync(asOfUtc, "anna");

        Assert.Equal(
            new[] { earlierHigh.Id, earlierLow.Id, laterDue.Id },
            overdue.Select(item => item.Id));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsCopy_ChangingItDoesNotAffectStoredTask()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem("Original");
        await repository.AddAsync(item);

        var firstLoad = await repository.GetByIdAsync(item.Id);
        firstLoad!.Title = "Damaged";
        var secondLoad = await repository.GetByIdAsync(item.Id);

        Assert.Equal("Original", secondLoad!.Title);
    }
}
