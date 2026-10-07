using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;

namespace UAI.ZeroVP.Tests;

public class InMemoryTodoRepositoryTests
{
    private static InMemoryTodoRepository CreateEmptyRepository() =>
        new(new InMemoryTodoOptions { SeedSampleData = false });

    private static TodoItem CreateItem() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test",
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task AddAsync_ThenGetById_ReturnsTheSameTask()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem();

        await repository.AddAsync(item);
        var loaded = await repository.GetByIdAsync(item.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Test", loaded.Title);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var repository = CreateEmptyRepository();

        var loaded = await repository.GetByIdAsync(Guid.NewGuid());

        Assert.Null(loaded);
    }

    [Fact]
    public async Task DeleteAsync_ExistingTask_ReturnsTrueAndRemovesIt()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem();
        await repository.AddAsync(item);

        var deleted = await repository.DeleteAsync(item.Id);

        Assert.True(deleted);
        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        var repository = CreateEmptyRepository();

        var deleted = await repository.DeleteAsync(Guid.NewGuid());

        Assert.False(deleted);
    }
}