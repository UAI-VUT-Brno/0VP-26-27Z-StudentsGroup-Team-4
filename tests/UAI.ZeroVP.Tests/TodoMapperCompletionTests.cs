using UAI.ZeroVP.API.Mapping;
using UAI.ZeroVP.Data.Entities;

namespace UAI.ZeroVP.Tests;

public class TodoMapperCompletionTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddDays(2);

    private static TodoItem CreateItem(bool isCompleted) => new()
    {
        Id = Guid.NewGuid(),
        Title = "A",
        CreatedAtUtc = Created,
        IsCompleted = isCompleted,
        CompletedAtUtc = isCompleted ? Created.AddHours(1) : null
    };

    [Fact]
    public void SetCompletionState_AlreadyCompleted_KeepsOriginalCompletionTime()
    {
        var item = CreateItem(isCompleted: true);
        var originalCompletedAt = item.CompletedAtUtc;

        item.SetCompletionState(isCompleted: true, Later);

        Assert.True(item.IsCompleted);
        Assert.Equal(originalCompletedAt, item.CompletedAtUtc);
    }

    [Fact]
    public void SetCompletionState_Reopening_ClearsCompletionTime()
    {
        var item = CreateItem(isCompleted: true);

        item.SetCompletionState(isCompleted: false, Later);

        Assert.False(item.IsCompleted);
        Assert.Null(item.CompletedAtUtc);
    }

    [Fact]
    public void SetCompletionState_OpenTaskStaysOpen_CompletionTimeStaysNull()
    {
        var item = CreateItem(isCompleted: false);

        item.SetCompletionState(isCompleted: false, Later);

        Assert.False(item.IsCompleted);
        Assert.Null(item.CompletedAtUtc);
    }

    [Fact]
    public void SetCompletionState_UpdatesUpdatedAtTime()
    {
        var item = CreateItem(isCompleted: false);

        item.SetCompletionState(isCompleted: true, Later);

        Assert.Equal(Later, item.UpdatedAtUtc);
    }
}