using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class TodoItemTests
{
    private static TodoItem CreateItem() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Original",
        Description = "Some description",
        Priority = TodoPriority.High,
        IsCompleted = true,
        CreatedAtUtc = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
        CompletedAtUtc = new DateTimeOffset(2026, 1, 16, 12, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public void Clone_CopiesAllValues()
    {
        var original = CreateItem();

        var clone = original.Clone();

        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.Title, clone.Title);
        Assert.Equal(original.Description, clone.Description);
        Assert.Equal(original.Priority, clone.Priority);
        Assert.Equal(original.IsCompleted, clone.IsCompleted);
        Assert.Equal(original.CreatedAtUtc, clone.CreatedAtUtc);
        Assert.Equal(original.CompletedAtUtc, clone.CompletedAtUtc);
    }

    [Fact]
    public void Clone_ReturnsDifferentInstance()
    {
        var original = CreateItem();

        var clone = original.Clone();

        Assert.NotSame(original, clone);
    }

    [Fact]
    public void Clone_ChangingCloneDoesNotChangeOriginal()
    {
        var original = CreateItem();

        var clone = original.Clone();
        clone.Title = "Changed";
        clone.IsCompleted = false;

        Assert.Equal("Original", original.Title);
        Assert.True(original.IsCompleted);
    }
}