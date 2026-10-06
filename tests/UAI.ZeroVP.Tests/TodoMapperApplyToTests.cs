using UAI.ZeroVP.API.Mapping;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class TodoMapperApplyToTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddDays(1);

    private static TodoItem CreateItem(bool isCompleted = false) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Old title",
        Priority = TodoPriority.Low,
        CreatedAtUtc = Created,
        IsCompleted = isCompleted,
        CompletedAtUtc = isCompleted ? Created.AddHours(1) : null
    };

    [Fact]
    public void ApplyTo_OverwritesFieldsAndTrimsText()
    {
        var item = CreateItem();
        var request = new UpdateTodoItemRequest
        {
            Title = "  New title  ",
            Description = "  New description  ",
            Priority = TodoPriority.High
        };

        request.ApplyTo(item, Later);

        Assert.Equal("New title", item.Title);
        Assert.Equal("New description", item.Description);
        Assert.Equal(TodoPriority.High, item.Priority);
        Assert.Equal(Later, item.UpdatedAtUtc);
    }

    [Fact]
    public void ApplyTo_IsCompletedTrue_MarksTaskCompleted()
    {
        var item = CreateItem(isCompleted: false);
        var request = new UpdateTodoItemRequest { Title = "A", IsCompleted = true };

        request.ApplyTo(item, Later);

        Assert.True(item.IsCompleted);
        Assert.Equal(Later, item.CompletedAtUtc);
    }

    [Fact]
    public void ApplyTo_IsCompletedFalse_ReopensCompletedTask()
    {
        var item = CreateItem(isCompleted: true);
        var request = new UpdateTodoItemRequest { Title = "A", IsCompleted = false };

        request.ApplyTo(item, Later);

        Assert.False(item.IsCompleted);
        Assert.Null(item.CompletedAtUtc);
    }

    [Fact]
    public void ApplyTo_DoesNotChangeIdOrCreatedTime()
    {
        var item = CreateItem();
        var originalId = item.Id;

        new UpdateTodoItemRequest { Title = "Different" }.ApplyTo(item, Later);

        Assert.Equal(originalId, item.Id);
        Assert.Equal(Created, item.CreatedAtUtc);
    }
}