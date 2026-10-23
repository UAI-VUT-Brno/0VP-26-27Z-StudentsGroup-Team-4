using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class InMemoryTodoRepositoryAddRangeTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstDue = new(2026, 2, 2, 9, 0, 0, TimeSpan.Zero);

    private static InMemoryTodoRepository CreateEmptyRepository() =>
        new(new InMemoryTodoOptions { SeedSampleData = false });

    private static TodoItem CreateItem(string title = "Task", int dueAfterWeeks = 0) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Priority = TodoPriority.Normal,
        CreatedAtUtc = Created,
        DueAtUtc = FirstDue.AddDays(7 * dueAfterWeeks)
    };

    [Fact]
    public async Task AddRangeAsync_StoresAllItemsAndReturnsThemInTheSameOrder()
    {
        var repository = CreateEmptyRepository();
        var batch = new[] { CreateItem("A"), CreateItem("B"), CreateItem("C") };

        var result = await repository.AddRangeAsync(batch);

        Assert.Equal(batch.Select(i => i.Id), result.Select(i => i.Id));
        Assert.Equal(3, await repository.CountAsync());
        foreach (var item in batch)
        {
            Assert.NotNull(await repository.GetByIdAsync(item.Id));
        }
    }

    [Fact]
    public async Task AddRangeAsync_PreservesDueDates()
    {
        var repository = CreateEmptyRepository();
        var batch = new[] { CreateItem(dueAfterWeeks: 0), CreateItem(dueAfterWeeks: 1) };

        var result = await repository.AddRangeAsync(batch);

        Assert.Equal(new DateTimeOffset?[] { FirstDue, FirstDue.AddDays(7) }, result.Select(i => i.DueAtUtc));
        var stored = await repository.GetByIdAsync(batch[1].Id);
        Assert.Equal(FirstDue.AddDays(7), stored!.DueAtUtc);
    }

    [Fact]
    public async Task AddRangeAsync_ReturnsCopies_ChangingThemDoesNotAffectStoredTasks()
    {
        var repository = CreateEmptyRepository();
        var batch = new[] { CreateItem("Original") };

        var result = await repository.AddRangeAsync(batch);
        result[0].Title = "Changed";

        var stored = await repository.GetByIdAsync(batch[0].Id);
        Assert.Equal("Original", stored!.Title);
    }

    [Fact]
    public async Task AddRangeAsync_ChangingInputAfterwardsDoesNotAffectStoredTasks()
    {
        var repository = CreateEmptyRepository();
        var batch = new[] { CreateItem("Original") };

        await repository.AddRangeAsync(batch);
        batch[0].Title = "Changed";

        var stored = await repository.GetByIdAsync(batch[0].Id);
        Assert.Equal("Original", stored!.Title);
    }

    [Fact]
    public async Task AddRangeAsync_EmptyBatch_ReturnsEmptyListAndStoresNothing()
    {
        var repository = CreateEmptyRepository();

        var result = await repository.AddRangeAsync(Array.Empty<TodoItem>());

        Assert.Empty(result);
        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public async Task AddRangeAsync_DuplicateIdInsideBatch_ThrowsAndStoresNothing()
    {
        var repository = CreateEmptyRepository();
        var first = CreateItem("First");
        var batch = new[]
        {
            first,
            CreateItem("Other"),
            new TodoItem { Id = first.Id, Title = "Same id as first", CreatedAtUtc = Created }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddRangeAsync(batch));

        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public async Task AddRangeAsync_IdAlreadyInStore_ThrowsAndLeavesStoreUnchanged()
    {
        var repository = CreateEmptyRepository();
        var existing = CreateItem("Existing");
        await repository.AddAsync(existing);
        var newItem = CreateItem("New");
        var batch = new[] { newItem, new TodoItem { Id = existing.Id, Title = "Collides", CreatedAtUtc = Created } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddRangeAsync(batch));

        Assert.Equal(1, await repository.CountAsync());
        Assert.Null(await repository.GetByIdAsync(newItem.Id));
        var stillThere = await repository.GetByIdAsync(existing.Id);
        Assert.Equal("Existing", stillThere!.Title);
    }

    [Fact]
    public async Task AddRangeAsync_Null_Throws()
    {
        var repository = CreateEmptyRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.AddRangeAsync(null!));
    }

    [Fact]
    public async Task AddRangeAsync_NullItem_ThrowsAndStoresNothing()
    {
        var repository = CreateEmptyRepository();
        var batch = new TodoItem[] { CreateItem("A"), null! };

        await Assert.ThrowsAsync<ArgumentException>(() => repository.AddRangeAsync(batch));

        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public async Task AddRangeAsync_CancelledToken_ThrowsAndStoresNothing()
    {
        var repository = CreateEmptyRepository();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => repository.AddRangeAsync(new[] { CreateItem() }, cts.Token));

        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public async Task GetAllAsync_TasksSharingCreationTime_AreListedByDueDate()
    {
        var repository = CreateEmptyRepository();
        // Added out of order on purpose; all three share priority and creation time.
        await repository.AddRangeAsync(new[]
        {
            CreateItem("Week 2", dueAfterWeeks: 2),
            CreateItem("Week 0", dueAfterWeeks: 0),
            CreateItem("Week 1", dueAfterWeeks: 1)
        });

        var titles = (await repository.GetAllAsync()).Select(i => i.Title).ToArray();

        Assert.Equal(new[] { "Week 0", "Week 1", "Week 2" }, titles);
    }
}
