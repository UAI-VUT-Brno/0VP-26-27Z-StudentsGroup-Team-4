using System.ComponentModel.DataAnnotations;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class WeeklyRequestValidationTests
{
    private static readonly DateTimeOffset FirstDue = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static CreateWeeklyTodoItemsRequest ValidRequest() => new()
    {
        Title = "Weekly report",
        FirstDueAtUtc = FirstDue,
        RepeatCount = 3
    };

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        return results;
    }

    private static bool Mentions(IEnumerable<ValidationResult> results, string memberName) =>
        results.Any(r => r.MemberNames.Contains(memberName));

    [Fact]
    public void ValidRequest_HasNoErrors()
    {
        Assert.Empty(Validate(ValidRequest()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(52)]
    public void RepeatCount_InsideRange_IsValid(int repeatCount)
    {
        var request = ValidRequest() with { RepeatCount = repeatCount };

        Assert.Empty(Validate(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(53)]
    [InlineData(1000)]
    public void RepeatCount_OutsideRange_IsInvalid(int repeatCount)
    {
        var request = ValidRequest() with { RepeatCount = repeatCount };

        Assert.True(Mentions(Validate(request), nameof(CreateWeeklyTodoItemsRequest.RepeatCount)));
    }

    [Fact]
    public void RepeatCount_NotSent_IsInvalid()
    {
        var request = new CreateWeeklyTodoItemsRequest { Title = "A", FirstDueAtUtc = FirstDue };

        Assert.True(Mentions(Validate(request), nameof(CreateWeeklyTodoItemsRequest.RepeatCount)));
    }

    [Fact]
    public void FirstDueDate_Missing_IsInvalid()
    {
        var request = ValidRequest() with { FirstDueAtUtc = null };

        Assert.True(Mentions(Validate(request), nameof(CreateWeeklyTodoItemsRequest.FirstDueAtUtc)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Title_EmptyOrWhitespace_IsInvalid(string title)
    {
        var request = ValidRequest() with { Title = title };

        Assert.True(Mentions(Validate(request), nameof(CreateWeeklyTodoItemsRequest.Title)));
    }

    [Fact]
    public void Title_LongerThan200Characters_IsInvalid()
    {
        var request = ValidRequest() with { Title = new string('a', 201) };

        Assert.True(Mentions(Validate(request), nameof(CreateWeeklyTodoItemsRequest.Title)));
    }

    [Fact]
    public void Title_Exactly200Characters_IsValid()
    {
        var request = ValidRequest() with { Title = new string('a', 200) };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Description_LongerThan1000Characters_IsInvalid()
    {
        var request = ValidRequest() with { Description = new string('a', 1001) };

        Assert.True(Mentions(Validate(request), nameof(CreateWeeklyTodoItemsRequest.Description)));
    }

    [Fact]
    public void Description_Exactly1000Characters_IsValid()
    {
        var request = ValidRequest() with { Description = new string('a', 1000) };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Priority_UnknownValue_IsInvalid()
    {
        var request = ValidRequest() with { Priority = (TodoPriority)99 };

        Assert.True(Mentions(Validate(request), nameof(CreateWeeklyTodoItemsRequest.Priority)));
    }

    [Fact]
    public void FirstDueDate_WithNonUtcOffset_IsValid()
    {
        var request = ValidRequest() with { FirstDueAtUtc = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(1)) };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void LastOccurrenceBeyondSupportedRange_IsInvalid()
    {
        // 5 repetitions = 35 days, but only 30 days remain before the end of the supported range.
        var request = ValidRequest() with
        {
            FirstDueAtUtc = DateTimeOffset.MaxValue.AddDays(-30),
            RepeatCount = 5
        };

        var errors = Validate(request);

        Assert.True(Mentions(errors, nameof(CreateWeeklyTodoItemsRequest.FirstDueAtUtc)));
    }

    [Fact]
    public void LastOccurrenceExactlyAtSupportedRange_IsValid()
    {
        // 52 repetitions = 364 days; the last occurrence lands exactly on the latest supported date.
        var request = ValidRequest() with
        {
            FirstDueAtUtc = DateTimeOffset.MaxValue.AddDays(-364),
            RepeatCount = 52
        };

        Assert.Empty(Validate(request));
    }
}
