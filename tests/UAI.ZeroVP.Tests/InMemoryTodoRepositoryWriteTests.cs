using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;

namespace UAI.ZeroVP.Tests;

public class InMemoryTodoRepositoryWriteTests
{
    private static InMemoryTodoRepository CreateEmptyRepository() =>
        new(new InMemoryTodoOptions { SeedSampleData = false });

    private static TodoItem CreateItem(string title = "Before") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task UpdateAsync_ExistingTask_ReturnsTrueAndStoresChange()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem();
        await repository.AddAsync(item);
        var changed = item.Clone();
        changed.Title = "After";

        var result = await repository.UpdateAsync(changed);

        Assert.True(result);
        var stored = await repository.GetByIdAsync(item.Id);
        Assert.Equal("After", stored!.Title);
    }

    [Fact]
    public async Task UpdateAsync_UnknownTask_ReturnsFalseAndDoesNotAddIt()
    {
        var repository = CreateEmptyRepository();

        var result = await repository.UpdateAsync(CreateItem());

        Assert.False(result);
        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public async Task AddAsync_DuplicateId_Throws()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem();
        await repository.AddAsync(item);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddAsync(item));
    }

    [Fact]
    public async Task AddAsync_Null_Throws()
    {
        var repository = CreateEmptyRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.AddAsync(null!));
    }
}