using UAI.ZeroVP.API.Mapping;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

/// <summary>Weekly date calculation and the shape of the created occurrences.</summary>
public class TodoMapperWeeklyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 10, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstDue = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static CreateWeeklyTodoItemsRequest CreateRequest(int repeatCount, DateTimeOffset? firstDue = null) => new()
    {
        Title = "Weekly report",
        Description = "Send it to the team.",
        Priority = TodoPriority.High,
        FirstDueAtUtc = firstDue ?? FirstDue,
        RepeatCount = repeatCount
    };

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    [InlineData(52, 53)]
    public void ToEntities_CreatesFirstOccurrencePlusRepeatCount(int repeatCount, int expectedTasks)
    {
        var entities = CreateRequest(repeatCount).ToEntities(Now);

        Assert.Equal(expectedTasks, entities.Count);
    }

    [Fact]
    public void ToEntities_FirstOccurrenceIsDueAtTheRequestedTime()
    {
        var entities = CreateRequest(3).ToEntities(Now);

        Assert.Equal(FirstDue, entities[0].DueAtUtc);
    }

    [Fact]
    public void ToEntities_OccurrencesAreExactlySevenDaysApart()
    {
        var entities = CreateRequest(3).ToEntities(Now);

        var dueDates = entities.Select(e => e.DueAtUtc!.Value).ToArray();
        for (var i = 1; i < dueDates.Length; i++)
        {
            Assert.Equal(TimeSpan.FromDays(7), dueDates[i] - dueDates[i - 1]);
        }
    }

    [Fact]
    public void ToEntities_MaximumRepeatCount_LastOccurrenceIs364DaysAfterTheFirst()
    {
        var entities = CreateRequest(52).ToEntities(Now);

        Assert.Equal(FirstDue.AddDays(364), entities[^1].DueAtUtc);
    }

    [Fact]
    public void ToEntities_EveryOccurrenceHasItsOwnId()
    {
        var entities = CreateRequest(10).ToEntities(Now);

        Assert.Equal(entities.Count, entities.Select(e => e.Id).Distinct().Count());
        Assert.DoesNotContain(entities, e => e.Id == Guid.Empty);
    }

    [Fact]
    public void ToEntities_CopiesTaskFieldsToEveryOccurrence()
    {
        var entities = CreateRequest(3).ToEntities(Now);

        Assert.All(entities, entity =>
        {
            Assert.Equal("Weekly report", entity.Title);
            Assert.Equal("Send it to the team.", entity.Description);
            Assert.Equal(TodoPriority.High, entity.Priority);
        });
    }

    [Fact]
    public void ToEntities_TrimsTitleAndDescription()
    {
        var request = CreateRequest(1) with { Title = "  Weekly report  ", Description = "  text  " };

        var entities = request.ToEntities(Now);

        Assert.All(entities, entity =>
        {
            Assert.Equal("Weekly report", entity.Title);
            Assert.Equal("text", entity.Description);
        });
    }

    [Fact]
    public void ToEntities_BlankDescription_BecomesNull()
    {
        var request = CreateRequest(1) with { Description = "   " };

        var entities = request.ToEntities(Now);

        Assert.All(entities, entity => Assert.Null(entity.Description));
    }

    [Fact]
    public void ToEntities_EveryOccurrenceStartsIncomplete()
    {
        var entities = CreateRequest(3).ToEntities(Now);

        Assert.All(entities, entity =>
        {
            Assert.False(entity.IsCompleted);
            Assert.Null(entity.CompletedAtUtc);
        });
    }

    [Fact]
    public void ToEntities_AllOccurrencesShareCreationAndUpdateTime()
    {
        var entities = CreateRequest(3).ToEntities(Now);

        Assert.All(entities, entity =>
        {
            Assert.Equal(Now, entity.CreatedAtUtc);
            Assert.Equal(Now, entity.UpdatedAtUtc);
        });
    }

    [Fact]
    public void ToEntities_CrossingMonthAndYearBoundary()
    {
        var request = CreateRequest(2, new DateTimeOffset(2026, 12, 25, 10, 0, 0, TimeSpan.Zero));

        var dueDates = request.ToEntities(Now).Select(e => e.DueAtUtc).ToArray();

        Assert.Equal(
            new DateTimeOffset?[]
            {
                new DateTimeOffset(2026, 12, 25, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 8, 10, 0, 0, TimeSpan.Zero)
            },
            dueDates);
    }

    [Fact]
    public void ToEntities_CrossingLeapDay()
    {
        var request = CreateRequest(2, new DateTimeOffset(2028, 2, 22, 8, 0, 0, TimeSpan.Zero));

        var dueDates = request.ToEntities(Now).Select(e => e.DueAtUtc).ToArray();

        Assert.Equal(
            new DateTimeOffset?[]
            {
                new DateTimeOffset(2028, 2, 22, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2028, 2, 29, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2028, 3, 7, 8, 0, 0, TimeSpan.Zero)
            },
            dueDates);
    }

    [Fact]
    public void ToEntities_IsNotAffectedByDaylightSavingTimeChange()
    {
        // In Central Europe the clocks go back on 2026-10-25. The calculation is done in UTC,
        // so the occurrences stay exactly 7 x 24 hours apart.
        var request = CreateRequest(2, new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero));

        var dueDates = request.ToEntities(Now).Select(e => e.DueAtUtc).ToArray();

        Assert.Equal(
            new DateTimeOffset?[]
            {
                new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero)
            },
            dueDates);
    }

    [Fact]
    public void ToEntities_NonUtcFirstDueDate_IsNormalizedToUtc()
    {
        // 01:00 at +02:00 is 23:00 UTC of the previous day.
        var request = CreateRequest(1, new DateTimeOffset(2026, 10, 30, 1, 0, 0, TimeSpan.FromHours(2)));

        var entities = request.ToEntities(Now);

        Assert.All(entities, entity => Assert.Equal(TimeSpan.Zero, entity.DueAtUtc!.Value.Offset));
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 23, 0, 0, TimeSpan.Zero), entities[0].DueAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 23, 0, 0, TimeSpan.Zero), entities[1].DueAtUtc);
    }

    [Fact]
    public void ToEntities_MissingFirstDueDate_Throws()
    {
        var request = new CreateWeeklyTodoItemsRequest { Title = "A", RepeatCount = 1 };

        Assert.Throws<ArgumentException>(() => request.ToEntities(Now));
    }

    [Fact]
    public void ToEntities_NullRequest_Throws()
    {
        CreateWeeklyTodoItemsRequest request = null!;

        Assert.Throws<ArgumentNullException>(() => request.ToEntities(Now));
    }
    [Fact]
    public void ToEntities_CopiesTrimmedUserIdToEveryOccurrence()
    {
        var request = CreateRequest(3) with { UserId = "  anna  " };

        var entities = request.ToEntities(Now);

        Assert.All(entities, e => Assert.Equal("anna", e.UserId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToEntities_BlankUserId_IsStoredAsNull(string? userId)
    {
        var request = CreateRequest(2) with { UserId = userId };

        var entities = request.ToEntities(Now);

        Assert.All(entities, e => Assert.Null(e.UserId));
    }
}
