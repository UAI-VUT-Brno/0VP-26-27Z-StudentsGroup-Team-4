using UAI.ZeroVP.Data.InMemory;

namespace UAI.ZeroVP.Tests;

public class InMemoryTodoRepositorySeedTests
{
    [Fact]
    public async Task Constructor_DefaultOptions_SeedsFourSampleTasks()
    {
        var repository = new InMemoryTodoRepository(new InMemoryTodoOptions());

        Assert.Equal(4, await repository.CountAsync());
    }

    [Fact]
    public async Task Constructor_SeedDisabled_StartsEmpty()
    {
        var repository = new InMemoryTodoRepository(new InMemoryTodoOptions { SeedSampleData = false });

        Assert.Equal(0, await repository.CountAsync());
        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task Seed_ContainsExactlyOneCompletedTask()
    {
        var repository = new InMemoryTodoRepository(new InMemoryTodoOptions());

        var completed = await repository.GetAllAsync(isCompleted: true);

        var item = Assert.Single(completed);
        Assert.NotNull(item.CompletedAtUtc);
    }
}