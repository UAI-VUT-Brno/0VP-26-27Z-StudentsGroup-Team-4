using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.API.Logging;

namespace UAI.ZeroVP.Tests.Logging;

/// <summary>Zápis záznamů do souboru: pole, formát, rolování, retence, odolnost.</summary>
public sealed class JsonFileLoggerProviderTests
{
    [Fact]
    public void Entry_ContainsAllRequiredFields()
    {
        using var harness = new LogHarness();
        var id = Guid.NewGuid();

        harness.CreateLogger("Test.Category")
            .LogInformation(LogEvents.TodoCreated, "Vytvořen úkol {TodoId} ({Title}).", id, "Napsat Dockerfile");

        var entry = Assert.Single(harness.Flush());

        // datum a čas
        var timestamp = entry.Str("timestamp")!;
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", timestamp);
        var parsed = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        Assert.InRange((DateTimeOffset.UtcNow - parsed).Duration(), TimeSpan.Zero, TimeSpan.FromMinutes(1));

        // identifikace události, výsledek, chyba
        Assert.Equal("Information", entry.Str("level"));
        Assert.Equal(3000, entry.Id());
        Assert.Equal("TodoCreated", entry.Str("event"));
        Assert.Equal("Success", entry.Str("result"));
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("error").ValueKind);

        // zpráva a doplňující údaje
        Assert.Equal($"Vytvořen úkol {id} (Napsat Dockerfile).", entry.Str("message"));
        Assert.Equal("Test.Category", entry.Str("category"));
        Assert.Equal(id.ToString(), entry.Props().GetProperty("TodoId").GetString());
        Assert.Equal("Napsat Dockerfile", entry.Props().GetProperty("Title").GetString());
    }

    [Fact]
    public void Entry_KeepsDiacriticsReadableInFile()
    {
        using var harness = new LogHarness();

        harness.CreateLogger("T").LogInformation("Příliš žluťoučký kůň");
        harness.Flush();

        Assert.Contains("Příliš žluťoučký kůň", harness.RawText);
        Assert.DoesNotContain("\\u", harness.RawText);
    }

    [Fact]
    public void Entry_WithoutEventId_HasIdZeroAndNullEvent()
    {
        using var harness = new LogHarness();

        harness.CreateLogger("T").LogInformation("bez identifikátoru");

        var entry = Assert.Single(harness.Flush());
        Assert.Equal(0, entry.Id());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("event").ValueKind);
    }

    [Theory]
    [InlineData(LogLevel.Information, "Success")]
    [InlineData(LogLevel.Warning, "Success")]
    [InlineData(LogLevel.Error, "Failure")]
    [InlineData(LogLevel.Critical, "Failure")]
    public void Result_IsDerivedFromLevel_WhenEventDoesNotSpecifyIt(LogLevel level, string expectedResult)
    {
        using var harness = new LogHarness();

        harness.CreateLogger("T").Log(level, "zpráva");

        var entry = Assert.Single(harness.Flush());
        Assert.Equal(expectedResult, entry.Str("result"));
    }

    [Fact]
    public void Exception_IsWrittenAsErrorSummaryAndFullStackTrace()
    {
        using var harness = new LogHarness();
        Exception exception;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        harness.CreateLogger("T").LogError(LogEvents.RequestFailed, exception, "Selhalo");

        var entry = Assert.Single(harness.Flush());
        Assert.Equal("Failure", entry.Str("result"));
        Assert.Equal("InvalidOperationException: boom", entry.Str("error"));
        Assert.Contains("System.InvalidOperationException: boom", entry.Str("exception"));
        Assert.Contains(nameof(Exception_IsWrittenAsErrorSummaryAndFullStackTrace), entry.Str("exception"));
    }

    [Fact]
    public void ResultAndError_FromTemplate_AreLiftedToTopLevel()
    {
        using var harness = new LogHarness();

        harness.CreateLogger("T").LogWarning("Zrušeno {Result} {Error}", "Cancelled", "Klient odešel");

        var entry = Assert.Single(harness.Flush());
        Assert.Equal("Cancelled", entry.Str("result"));
        Assert.Equal("Klient odešel", entry.Str("error"));
        Assert.False(entry.TryGetProperty("properties", out _), "Result a Error se nemají opakovat v properties.");
    }

    [Fact]
    public void NewlineInValue_CannotForgeAdditionalLogLine()
    {
        using var harness = new LogHarness();
        const string malicious = "/a\n{\"eventId\":9999,\"event\":\"Fake\"}";

        harness.CreateLogger("T").LogInformation("Cesta {Path}", malicious);

        var entry = Assert.Single(harness.Flush()); // jeden záznam = jeden řádek
        Assert.Equal(malicious, entry.Props().GetProperty("Path").GetString());
        Assert.NotEqual(9999, entry.Id());
    }

    [Fact]
    public void Dispose_FlushesAllQueuedEntries_InOrder()
    {
        using var harness = new LogHarness();
        var logger = harness.CreateLogger("T");

        for (var i = 0; i < 500; i++)
        {
            logger.LogInformation("Záznam {N}", i);
        }

        var entries = harness.Flush();

        Assert.Equal(
            Enumerable.Range(0, 500),
            entries.Select(entry => entry.Props().GetProperty("N").GetInt32()));
    }

    [Fact]
    public void ParallelWriters_ProduceOnlyValidIntactLines()
    {
        using var harness = new LogHarness();
        var logger = harness.CreateLogger("T");

        Parallel.For(0, 1600, i => logger.LogInformation(LogEvents.RequestCompleted, "Záznam {N}", i));

        var entries = harness.Flush(); // ReadEntries by při poškozeném řádku vyhodil JsonException

        Assert.Equal(1600, entries.Count);
        Assert.Equal(1600, entries.Select(entry => entry.Props().GetProperty("N").GetInt32()).Distinct().Count());
    }

    [Fact]
    public async Task NewUtcDay_StartsNewFile_AndTimestampComesFromClock()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 7, 23, 59, 0, TimeSpan.Zero));
        using var harness = new LogHarness(time: clock);
        var logger = harness.CreateLogger("T");
        var day1 = Path.Combine(harness.Dir, "zerovp-20261007.log");

        logger.LogInformation("první");
        await LogFiles.WaitUntilAsync(() => File.Exists(day1) && LogFiles.ReadLines(day1).Length == 1);

        clock.Set(new DateTimeOffset(2026, 10, 8, 0, 1, 0, TimeSpan.Zero));
        logger.LogInformation("druhý");
        harness.Flush();

        var names = Directory.GetFiles(harness.Dir).Select(file => Path.GetFileName(file)!).OrderBy(name => name).ToArray();
        Assert.Equal(new[] { "zerovp-20261007.log", "zerovp-20261008.log" }, names);

        var first = Assert.Single(LogFiles.ReadLines(day1).Select(line => JsonDocument.Parse(line).RootElement));
        Assert.Equal("2026-10-07T23:59:00.000Z", first.Str("timestamp"));
    }

    [Fact]
    public void Retention_KeepsNewestFiles_AndLeavesForeignFilesAlone()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        using var harness = new LogHarness(options => options.RetainedFileCount = 2, clock);

        foreach (var day in new[] { "20260101", "20260102", "20260103", "20260104", "20260105" })
        {
            File.WriteAllText(Path.Combine(harness.Dir, $"zerovp-{day}.log"), "{}\n");
        }

        File.WriteAllText(Path.Combine(harness.Dir, "other.log"), "cizí soubor");

        harness.CreateLogger("T").LogInformation("dnes");
        harness.Flush();

        var names = Directory.GetFiles(harness.Dir).Select(file => Path.GetFileName(file)!).OrderBy(name => name).ToArray();
        Assert.Equal(new[] { "other.log", "zerovp-20260105.log", "zerovp-20261007.log" }, names);
    }

    [Fact]
    public void UnwritableDirectory_NeverThrowsIntoTheApplication()
    {
        using var temp = new TempDir();
        var blocker = Path.Combine(temp.FullPath, "blocker");
        File.WriteAllText(blocker, "soubor místo složky");
        var options = new FileLoggingOptions { Directory = Path.Combine(blocker, "logs") };

        var exception = Record.Exception(() =>
        {
            using var provider = new JsonFileLoggerProvider(options, temp.FullPath);
            provider.CreateLogger("T").LogInformation("nejde zapsat");
        });

        Assert.Null(exception);
    }
}
