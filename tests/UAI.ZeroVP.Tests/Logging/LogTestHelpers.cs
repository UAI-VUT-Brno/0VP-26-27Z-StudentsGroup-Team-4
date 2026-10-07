using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.API.Logging;

namespace UAI.ZeroVP.Tests.Logging;

/// <summary>Dočasná složka, která se po testu smaže.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        FullPath = Path.Combine(Path.GetTempPath(), "zerovp-logtests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Čas, který řídí test – umožňuje "přeskočit" na další den.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start) => _now = start;

    public void Set(DateTimeOffset now)
    {
        lock (_lock)
        {
            _now = now;
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }
}

/// <summary>
/// Skutečný souborový poskytovatel logu ve dočasné složce + továrna na loggery.
/// <see cref="Flush"/> poskytovatele zavře (tím se vyprázdní fronta) a vrátí zapsané záznamy.
/// </summary>
internal sealed class LogHarness : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly LoggerFactory _factory;

    public LogHarness(Action<FileLoggingOptions>? configure = null, TimeProvider? time = null)
    {
        var options = new FileLoggingOptions { Directory = _temp.FullPath };
        configure?.Invoke(options);

        Provider = new JsonFileLoggerProvider(options, _temp.FullPath, time);
        _factory = new LoggerFactory(new ILoggerProvider[] { Provider });
    }

    public string Dir => _temp.FullPath;

    public JsonFileLoggerProvider Provider { get; }

    /// <summary>Obsah všech souborů po posledním <see cref="Flush"/>.</summary>
    public string RawText { get; private set; } = string.Empty;

    public ILogger CreateLogger(string category) => _factory.CreateLogger(category);

    public ILogger<T> Logger<T>() => _factory.CreateLogger<T>();

    public List<JsonElement> Flush()
    {
        _factory.Dispose();
        Provider.Dispose();

        RawText = LogFiles.ReadRawText(Dir);
        return LogFiles.ReadEntries(Dir);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Provider.Dispose();
        _temp.Dispose();
    }
}

/// <summary>Čtení a prohlížení logovacích souborů v testech.</summary>
internal static class LogFiles
{
    public static string[] ReadLines(string path)
    {
        // FileShare.ReadWrite: soubor může být v tu chvíli otevřený zapisovačem.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    public static string ReadRawText(string dir) =>
        string.Concat(LogFilePaths(dir).Select(path => string.Join('\n', ReadLines(path)) + "\n"));

    public static List<JsonElement> ReadEntries(string dir) =>
        LogFilePaths(dir)
            .SelectMany(ReadLines)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();

    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!IsTrue(condition))
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException("Podmínka nebyla splněna v časovém limitu.");
            }

            await Task.Delay(20);
        }
    }

    public static string? Str(this JsonElement entry, string name) => entry.GetProperty(name).GetString();

    public static int Id(this JsonElement entry) => entry.GetProperty("eventId").GetInt32();

    public static JsonElement Props(this JsonElement entry) => entry.GetProperty("properties");

    private static string[] LogFilePaths(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return Array.Empty<string>();
        }

        var paths = Directory.GetFiles(dir, "zerovp-*.log");
        Array.Sort(paths, StringComparer.Ordinal);

        return paths;
    }

    private static bool IsTrue(Func<bool> condition)
    {
        try
        {
            return condition();
        }
        catch (IOException)
        {
            return false;
        }
    }
}
