using UAI.ZeroVP.API.Logging;

namespace UAI.ZeroVP.Tests.Logging;

/// <summary>0VP-T4-F14: escaping of user-supplied text before it is put into a log message.</summary>
public sealed class LogSanitizerTests
{
    [Theory]
    [InlineData("a\r\nb", @"a\r\nb")]
    [InlineData("a\nb", @"a\nb")]
    [InlineData("a\rb", @"a\rb")]
    [InlineData("a\tb", @"a\tb")]
    [InlineData("a\u001bb", @"a\u001bb")] // ESC, used for ANSI escape sequences in terminals
    [InlineData("a\u0000b", @"a\u0000b")]
    [InlineData("a\u0085b", @"a\u0085b")] // NEL
    [InlineData("a\x2028b\x2029c", @"a\u2028b\u2029c")]
    public void ForLog_EscapesLineBreaksAndControlCharacters(string input, string expected)
    {
        Assert.Equal(expected, LogSanitizer.ForLog(input));
    }

    [Theory]
    [InlineData("Napsat Dockerfile")]
    [InlineData("Příliš žluťoučký kůň \"úpěl\" ďábelské ódy {0} <b>")]
    [InlineData("")]
    public void ForLog_LeavesOrdinaryTextUnchanged(string input)
    {
        Assert.Same(input, LogSanitizer.ForLog(input));
    }

    [Fact]
    public void ForLog_Null_ReturnsNull()
    {
        Assert.Null(LogSanitizer.ForLog(null));
    }
}
