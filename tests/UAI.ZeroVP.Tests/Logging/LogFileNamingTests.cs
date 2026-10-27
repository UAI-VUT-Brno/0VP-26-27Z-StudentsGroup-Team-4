using UAI.ZeroVP.API.Logging;

namespace UAI.ZeroVP.Tests.Logging;

/// <summary>Log file naming: instance id is part of the name only when configured.</summary>
public sealed class LogFileNamingTests
{
    [Fact]
    public void BuildPrefix_WithoutIncludeInstanceId_KeepsOriginalPrefix()
    {
        Assert.Equal("zerovp", LogFileNaming.BuildPrefix("zerovp", "api-1", includeInstanceId: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BuildPrefix_WithBlankInstanceId_KeepsOriginalPrefix(string? instanceId)
    {
        Assert.Equal("zerovp", LogFileNaming.BuildPrefix("zerovp", instanceId, includeInstanceId: true));
    }

    [Fact]
    public void BuildPrefix_WithInstanceId_AppendsIt()
    {
        Assert.Equal("zerovp-api-1", LogFileNaming.BuildPrefix("zerovp", "api-1", includeInstanceId: true));
    }

    [Fact]
    public void BuildFileName_AppendsUtcDate()
    {
        var name = LogFileNaming.BuildFileName("zerovp-api-1", new DateTimeOffset(2026, 10, 7, 23, 59, 0, TimeSpan.Zero));

        Assert.Equal("zerovp-api-1-20261007.log", name);
    }

    [Theory]
    [InlineData("api-1", "api-1")]
    [InlineData("PC_01.local", "PC_01.local")]
    [InlineData("a/b\\c:d", "a_b_c_d")]
    [InlineData("my instance", "my_instance")]
    [InlineData("../x", ".._x")]
    public void Sanitize_ReplacesCharactersUnsafeForFileNames(string input, string expected)
    {
        Assert.Equal(expected, LogFileNaming.Sanitize(input));
    }

    [Fact]
    public void Sanitize_TruncatesVeryLongIds()
    {
        Assert.Equal(80, LogFileNaming.Sanitize(new string('x', 500)).Length);
    }

    [Theory]
    [InlineData("zerovp-api-20261007.log", "zerovp-api", true)]
    [InlineData("zerovp-api-2-20261007.log", "zerovp-api", false)]   // another instance
    [InlineData("zerovp-20261007.log", "zerovp", true)]
    [InlineData("zerovp-api-20261007.log", "zerovp", false)]          // log of an instance, not of the default name
    [InlineData("other.log", "zerovp", false)]
    [InlineData("zerovp-20261007.log.bak", "zerovp", false)]
    public void IsLogFileName_MatchesOnlyOwnDailyFiles(string fileName, string prefix, bool expected)
    {
        Assert.Equal(expected, LogFileNaming.IsLogFileName(fileName, prefix));
    }
}
