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
        var request = new CreateTodoItemRequest { Title = "  Write test  " };

        var entity = request.ToEntity(Now);

        Assert.Equal("Write test", entity.Title);
    }

    [Fact]
    public void ToEntity_BlankDescription_BecomesNull()
    {
        var request = new CreateTodoItemRequest { Title = "A", Description = "   " };

        var entity = request.ToEntity(Now);

        Assert.Null(entity.Description);
    }

    [Fact]
    public void ToEntity_NewTask_IsNotCompleted()
    {
        var request = new CreateTodoItemRequest { Title = "A" };

        var entity = request.ToEntity(Now);

        Assert.False(entity.IsCompleted);
        Assert.Null(entity.CompletedAtUtc);
        Assert.Equal(Now, entity.CreatedAtUtc);
    }

    [Fact]
    public void SetCompletionState_Completing_SetsCompletedAt()
    {
        var entity = new TodoItem { Id = Guid.NewGuid(), Title = "A", CreatedAtUtc = Now };

        entity.SetCompletionState(isCompleted: true, Now);

        Assert.True(entity.IsCompleted);
        Assert.Equal(Now, entity.CompletedAtUtc);
    }
}