using System.ComponentModel.DataAnnotations;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class RequestValidationTests
{
    private static bool IsValid(object model) =>
        Validator.TryValidateObject(model, new ValidationContext(model), new List<ValidationResult>(), validateAllProperties: true);

    [Fact]
    public void CreateRequest_EmptyTitle_IsInvalid()
    {
        var request = new CreateTodoItemRequest { Title = "" };

        Assert.False(IsValid(request));
    }

    [Fact]
    public void CreateRequest_TitleLongerThan200Characters_IsInvalid()
    {
        var request = new CreateTodoItemRequest { Title = new string('a', 201) };

        Assert.False(IsValid(request));
    }
}