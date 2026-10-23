using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;

namespace UAI.ZeroVP.Tests;

public class InMemoryTodoRepositoryUserTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static InMemoryTodoRepository CreateEmptyRepository() =>
        new(new InMemoryTodoOptions { SeedSampleData = false });

    private static TodoItem CreateItem(string title, string? userId = null, bool isCompleted = false) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        CreatedAtUtc = Created,
        IsCompleted = isCompleted,
        UserId = userId
    };

    [Fact]
    public async Task AddAsync_PersistsUserId()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem("A", userId: "anna");

        await repository.AddAsync(item);
        var loaded = await repository.GetByIdAsync(item.Id);

        Assert.Equal("anna", loaded!.UserId);
    }

    [Fact]
    public async Task AddAsync_WithoutUserId_StoresNull()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem("A");

        await repository.AddAsync(item);
        var loaded = await repository.GetByIdAsync(item.Id);

        Assert.Null(loaded!.UserId);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChangedUserId()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem("A", userId: "anna");
        await repository.AddAsync(item);

        var changed = (await repository.GetByIdAsync(item.Id))!;
        changed.UserId = "petr";
        await repository.UpdateAsync(changed);
        var loaded = await repository.GetByIdAsync(item.Id);

        Assert.Equal("petr", loaded!.UserId);
    }

    [Fact]
    public async Task GetAllAsync_FiltersByUserId()
    {
        var repository = CreateEmptyRepository();
        await repository.AddAsync(CreateItem("Anna 1", userId: "anna"));
        await repository.AddAsync(CreateItem("Anna 2", userId: "anna"));
        await repository.AddAsync(CreateItem("Petr 1", userId: "petr"));
        await repository.AddAsync(CreateItem("Nobody"));

        var annas = await repository.GetAllAsync(userId: "anna");
        var all = await repository.GetAllAsync();

        Assert.Equal(2, annas.Count);
        Assert.All(annas, item => Assert.Equal("anna", item.UserId));
        Assert.Equal(4, all.Count);
    }

    [Fact]
    public async Task GetAllAsync_UserIdFilterIgnoresCase()
    {
        var repository = CreateEmptyRepository();
        await repository.AddAsync(CreateItem("A", userId: "Anna@Example.com"));

        var result = await repository.GetAllAsync(userId: "anna@example.com");

        Assert.Single(result);
    }

    [Fact]
    public async Task GetAllAsync_UnknownUserId_ReturnsEmptyList()
    {
        var repository = CreateEmptyRepository();
        await repository.AddAsync(CreateItem("A", userId: "anna"));

        var result = await repository.GetAllAsync(userId: "nobody");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllAsync_CombinesUserIdAndCompletionFilter()
    {
        var repository = CreateEmptyRepository();
        await repository.AddAsync(CreateItem("Anna done", userId: "anna", isCompleted: true));
        await repository.AddAsync(CreateItem("Anna open", userId: "anna"));
        await repository.AddAsync(CreateItem("Petr done", userId: "petr", isCompleted: true));

        var result = await repository.GetAllAsync(isCompleted: true, userId: "anna");

        var item = Assert.Single(result);
        Assert.Equal("Anna done", item.Title);
    }
}