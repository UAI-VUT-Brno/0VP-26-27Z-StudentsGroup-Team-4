using System.ComponentModel.DataAnnotations;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class RequestValidationBoundaryTests
{
    private static bool IsValid(object model) =>
        Validator.TryValidateObject(model, new ValidationContext(model), new List<ValidationResult>(), validateAllProperties: true);

    [Fact]
    public void CreateRequest_ValidRequest_IsValid()
    {
        var request = new CreateTodoItemRequest { Title = "OK", Description = "Some text", Priority = TodoPriority.High };

        Assert.True(IsValid(request));
    }

    [Fact]
    public void CreateRequest_WhitespaceTitle_IsInvalid()
    {
        var request = new CreateTodoItemRequest { Title = "   " };

        Assert.False(IsValid(request));
    }

    [Fact]
    public void CreateRequest_Title200Characters_IsValid()
    {
        var request = new CreateTodoItemRequest { Title = new string('a', 200) };

        Assert.True(IsValid(request));
    }

    [Fact]
    public void CreateRequest_DescriptionLongerThan1000Characters_IsInvalid()
    {
        var request = new CreateTodoItemRequest { Title = "A", Description = new string('b', 1001) };

        Assert.False(IsValid(request));
    }

    [Fact]
    public void CreateRequest_UndefinedPriority_IsInvalid()
    {
        var request = new CreateTodoItemRequest { Title = "A", Priority = (TodoPriority)99 };

        Assert.False(IsValid(request));
    }

    [Fact]
    public void UpdateRequest_EmptyTitle_IsInvalid()
    {
        var request = new UpdateTodoItemRequest { Title = "" };

        Assert.False(IsValid(request));
    }
}