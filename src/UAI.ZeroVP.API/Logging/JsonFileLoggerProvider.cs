using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace UAI.ZeroVP.API.Logging;

/// <summary>
/// Poskytovatel logování, který zapisuje záznamy jako JSON Lines do denních souborů.
/// <para>
/// Zápis na disk řeší jediné pozadové vlákno: požadavky jen vloží řádek do fronty
/// a pokračují dál, takže pomalý disk nebrzdí API. Při ukončení aplikace
/// (<see cref="Dispose"/>) se fronta vyprázdní, takže se do souboru dostane i poslední
/// událost <c>ApplicationStopped</c>.
/// </para>
/// <para>
/// Chyba při zápisu (plný disk, chybějící práva) aplikaci nikdy neshodí –
/// vypíše se na standardní chybový výstup a zápis se zkusí znovu u další dávky.
/// </para>
/// </summary>
[ProviderAlias("JsonFile")]
public sealed class JsonFileLoggerProvider : ILoggerProvider
{
    private readonly FileLoggingOptions _options;
    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<string> _queue;
    private readonly Task _writerTask;
    private int _disposed;

    /// <summary>Vytvoří poskytovatele a spustí zapisovací vlákno.</summary>
    /// <param name="options">Nastavení souborového logu.</param>
    /// <param name="contentRootPath">Základ pro relativní cestu ke složce s logy.</param>
    /// <param name="timeProvider">Zdroj času; výchozí je systémový.</param>
    public JsonFileLoggerProvider(FileLoggingOptions options, string contentRootPath, TimeProvider? timeProvider = null)
    {
        _options = options;
        _directory = Path.GetFullPath(options.Directory, contentRootPath);
        _timeProvider = timeProvider ?? TimeProvider.System;

        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(Math.Max(1, options.QueueCapacity))
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });

        _writerTask = Task.Run(WriteLoopAsync);
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new JsonFileLogger(categoryName, this);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // Uzavřít frontu a počkat, až zapisovač zapíše zbytek a soubor zavře.
        _queue.Writer.TryComplete();

        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Chyba zapisovače už byla ohlášena na stderr.
        }
    }

    internal TimeProvider Clock => _timeProvider;

    internal void Enqueue(string line) => _queue.Writer.TryWrite(line);

    private async Task WriteLoopAsync()
    {
        StreamWriter? writer = null;
        string? currentPath = null;

        try
        {
            while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                try
                {
                    while (_queue.Reader.TryRead(out var line))
                    {
                        var path = GetFilePath();

                        // Nový den (nebo první záznam): přepnout na nový soubor a uklidit staré.
                        if (!string.Equals(path, currentPath, StringComparison.Ordinal))
                        {
                            writer?.Dispose();
                            writer = null;
                            writer = OpenWriter(path);
                            currentPath = path;
                            ApplyRetention();
                        }

                        writer!.WriteLine(line);
                    }

                    // Jednou za dávku – záznamy jsou na disku hned, ne až při zavření souboru.
                    writer?.Flush();
                }
                catch (Exception exception)
                {
                    ReportFailure(exception);
                    writer?.Dispose();
                    writer = null;
                    currentPath = null; // příští dávka zkusí soubor otevřít znovu
                }
            }
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private string GetFilePath()
    {
        var day = _timeProvider.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        return Path.Combine(_directory, $"{_options.FileNamePrefix}-{day}.log");
    }

    private StreamWriter OpenWriter(string path)
    {
        Directory.CreateDirectory(_directory);

        // FileShare.ReadWrite: soubor jde číst (tail -f, editor) i za běhu aplikace.
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n" // stejný konec řádku na Windows i v Linuxovém kontejneru
        };
    }

    private void ApplyRetention()
    {
        if (_options.RetainedFileCount <= 0)
        {
            return;
        }

        try
        {
            // Název obsahuje datum ve tvaru yyyyMMdd, takže abecední řazení = chronologické.
            var obsolete = new DirectoryInfo(_directory)
                .GetFiles($"{_options.FileNamePrefix}-*.log")
                .OrderByDescending(file => file.Name, StringComparer.Ordinal)
                .Skip(_options.RetainedFileCount);

            foreach (var file in obsolete)
            {
                try
                {
                    file.Delete();
                }
                catch (IOException)
                {
                    // Soubor je zrovna otevřený jinde – smaže se při příštím přepnutí.
                }
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private static void ReportFailure(Exception exception) =>
        Console.Error.WriteLine($"[JsonFileLogger] Zápis do souborového logu selhal: {exception.GetType().Name}: {exception.Message}");
}

/// <summary>Logger jedné kategorie; jen formátuje záznam a předá ho poskytovateli.</summary>
internal sealed class JsonFileLogger : ILogger
{
    private readonly string _category;
    private readonly JsonFileLoggerProvider _provider;

    public JsonFileLogger(string category, JsonFileLoggerProvider provider)
    {
        _category = category;
        _provider = provider;
    }

    // Rozsahy (scopes) se do souboru nezapisují – vše potřebné je přímo v záznamu.
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        try
        {
            var line = JsonLogEntryFormatter.Format(
                _provider.Clock.GetUtcNow(),
                _category,
                logLevel,
                eventId,
                state,
                exception,
                formatter(state, exception));

            _provider.Enqueue(line);
        }
        catch (Exception)
        {
            // Logování nesmí shodit aplikaci kvůli vadnému záznamu.
        }
    }
}
