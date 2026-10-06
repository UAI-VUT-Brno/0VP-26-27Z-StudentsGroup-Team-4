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