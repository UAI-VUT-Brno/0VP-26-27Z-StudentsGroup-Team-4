using UAI.ZeroVP.API.Mapping;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class TodoMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToEntity_TrimsTitle()
    {
        var request = new CreateTodoItemRequest { Title = "  Write test  ", UserId = "user-1" };

        var entity = request.ToEntity(Now);

        Assert.Equal("Write test", entity.Title);
    }

    [Fact]
    public void ToEntity_BlankDescription_BecomesNull()
    {
        var request = new CreateTodoItemRequest { Title = "A", UserId = "user-1", Description = "   " };

        var entity = request.ToEntity(Now);

        Assert.Null(entity.Description);
    }

    [Fact]
    public void ToEntity_NewTask_IsNotCompleted()
    {
        var request = new CreateTodoItemRequest { Title = "A", UserId = "user-1" };

        var entity = request.ToEntity(Now);

        Assert.Equal("user-1", entity.UserId);
        Assert.False(entity.IsCompleted);
        Assert.Null(entity.CompletedAtUtc);
        Assert.Equal(Now, entity.CreatedAtUtc);
    }

    [Fact]
    public void ToDto_AndClone_PreserveUserId()
    {
        var entity = new CreateTodoItemRequest { Title = "A", UserId = "user-1" }.ToEntity(Now);

        Assert.Equal("user-1", entity.Clone().UserId);
        Assert.Equal("user-1", entity.ToDto().UserId);
    }

    [Fact]
    public void SetCompletionState_Completing_SetsCompletedAt()
    {
        var entity = new TodoItem { Id = Guid.NewGuid(), UserId = "user-1", Title = "A", CreatedAtUtc = Now };

        entity.SetCompletionState(isCompleted: true, Now);

        Assert.True(entity.IsCompleted);
        Assert.Equal(Now, entity.CompletedAtUtc);
    }
}