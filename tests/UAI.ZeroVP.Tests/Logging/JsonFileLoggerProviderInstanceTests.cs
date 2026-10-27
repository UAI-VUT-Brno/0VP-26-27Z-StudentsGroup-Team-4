using Microsoft.Extensions.Logging;
using UAI.ZeroVP.API.Logging;

namespace UAI.ZeroVP.Tests.Logging;

/// <summary>Concurrent instances must not write to (or clean up) each other's log files.</summary>
public sealed class JsonFileLoggerProviderInstanceTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static string[] FileNames(string directory) =>
        Directory.GetFiles(directory).Select(path => Path.GetFileName(path)!).OrderBy(name => name, StringComparer.Ordinal).ToArray();

    private static void WriteOneEntry(FileLoggingOptions options, string root, string? instanceId, string message)
    {
        // Dispose of the provider flushes the queue to disk.
        using var provider = new JsonFileLoggerProvider(options, root, new ManualTimeProvider(Day), instanceId);
        provider.CreateLogger("T").LogInformation("{Message}", message);
    }

    [Fact]
    public void WithIncludeInstanceId_InstanceIdIsPartOfTheFileName()
    {
        using var temp = new TempDir();
        var options = new FileLoggingOptions { Directory = temp.FullPath, IncludeInstanceId = true };

        WriteOneEntry(options, temp.FullPath, "api-1", "x");

        Assert.Equal(new[] { "zerovp-api-1-20261007.log" }, FileNames(temp.FullPath));
    }

    [Fact]
    public void WithoutIncludeInstanceId_FileNameIsTheOriginalOne()
    {
        // Default configuration: a single instance behaves exactly as before.
        using var temp = new TempDir();
        var options = new FileLoggingOptions { Directory = temp.FullPath };

        WriteOneEntry(options, temp.FullPath, "api-1", "x");

        Assert.False(options.IncludeInstanceId);
        Assert.Equal(new[] { "zerovp-20261007.log" }, FileNames(temp.FullPath));
    }

    [Fact]
    public void InstanceId_IsSanitizedInFileName()
    {
        using var temp = new TempDir();
        var options = new FileLoggingOptions { Directory = temp.FullPath, IncludeInstanceId = true };

        WriteOneEntry(options, temp.FullPath, "a/b:c", "x");

        Assert.Equal(new[] { "zerovp-a_b_c-20261007.log" }, FileNames(temp.FullPath));
    }

    [Fact]
    public void TwoInstances_SharingOneDirectory_WriteToSeparateFiles()
    {
        using var temp = new TempDir();
        var options = new FileLoggingOptions { Directory = temp.FullPath, IncludeInstanceId = true };

        using (var first = new JsonFileLoggerProvider(options, temp.FullPath, new ManualTimeProvider(Day), "api-1"))
        using (var second = new JsonFileLoggerProvider(options, temp.FullPath, new ManualTimeProvider(Day), "api-2"))
        {
            // Both providers are alive at the same time, like two running processes.
            first.CreateLogger("T").LogInformation("{Message}", "from-first");
            second.CreateLogger("T").LogInformation("{Message}", "from-second");
        }

        var files = FileNames(temp.FullPath);
        Assert.Equal(new[] { "zerovp-api-1-20261007.log", "zerovp-api-2-20261007.log" }, files);

        var firstText = string.Join('\n', LogFiles.ReadLines(Path.Combine(temp.FullPath, files[0])));
        var secondText = string.Join('\n', LogFiles.ReadLines(Path.Combine(temp.FullPath, files[1])));
        Assert.Contains("from-first", firstText);
        Assert.DoesNotContain("from-second", firstText);
        Assert.Contains("from-second", secondText);
        Assert.DoesNotContain("from-first", secondText);
    }

    [Fact]
    public void Retention_NeverDeletesFilesOfAnotherInstance()
    {
        using var temp = new TempDir();
        var options = new FileLoggingOptions { Directory = temp.FullPath, IncludeInstanceId = true, RetainedFileCount = 1 };

        // "api" and "api-2" are different instances, although "zerovp-api-*.log" would match both.
        foreach (var day in new[] { "20260101", "20260102", "20260103" })
        {
            File.WriteAllText(Path.Combine(temp.FullPath, $"zerovp-api-{day}.log"), "{}\n");
            File.WriteAllText(Path.Combine(temp.FullPath, $"zerovp-api-2-{day}.log"), "{}\n");
        }

        WriteOneEntry(options, temp.FullPath, "api", "x");

        Assert.Equal(
            new[]
            {
                "zerovp-api-2-20260101.log",   // everything of "api-2" is untouched
                "zerovp-api-2-20260102.log",
                "zerovp-api-2-20260103.log",
                "zerovp-api-20261007.log"      // the only file of "api" that is kept
            },
            FileNames(temp.FullPath));
    }
}