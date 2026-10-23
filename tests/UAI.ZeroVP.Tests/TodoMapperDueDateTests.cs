using UAI.ZeroVP.API.Mapping;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

/// <summary>The optional due date must survive every mapping step and must never be lost by an update.</summary>
public class TodoMapperDueDateTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new(2026, 1, 22, 9, 30, 0, TimeSpan.Zero);

    private static TodoItem CreateEntity(DateTimeOffset? dueAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        Title = "A",
        CreatedAtUtc = Now,
        DueAtUtc = dueAtUtc
    };

    [Fact]
    public void ToDto_TaskWithDueDate_CopiesIt()
    {
        var dto = CreateEntity(Due).ToDto();

        Assert.Equal(Due, dto.DueAtUtc);
    }

    [Fact]
    public void ToDto_TaskWithoutDueDate_HasNullDueDate()
    {
        var dto = CreateEntity(null).ToDto();

        Assert.Null(dto.DueAtUtc);
    }

    [Fact]
    public void ToEntity_RegularCreateRequest_HasNoDueDate()
    {
        var entity = new CreateTodoItemRequest { Title = "A" }.ToEntity(Now);

        Assert.Null(entity.DueAtUtc);
    }

    [Fact]
    public void ApplyTo_UpdateRequest_KeepsExistingDueDate()
    {
        var entity = CreateEntity(Due);
        var request = new UpdateTodoItemRequest { Title = "B", IsCompleted = false };

        request.ApplyTo(entity, Now.AddDays(1));

        Assert.Equal(Due, entity.DueAtUtc);
    }

    [Fact]
    public void SetCompletionState_KeepsExistingDueDate()
    {
        var entity = CreateEntity(Due);

        entity.SetCompletionState(isCompleted: true, Now.AddDays(1));

        Assert.Equal(Due, entity.DueAtUtc);
    }
}
