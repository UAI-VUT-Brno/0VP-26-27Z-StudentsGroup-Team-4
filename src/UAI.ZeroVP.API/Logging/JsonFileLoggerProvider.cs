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
/// <para>
/// Když je fronta plná (disk nestíhá), nové záznamy se zahodí, aby požadavky nečekaly.
/// Zahozené záznamy se ale počítají (<see cref="DroppedCount"/>). Po nejbližší zapsané dávce
/// se do souboru i na standardní chybový výstup zapíše varování <c>LogEntriesDropped</c>
/// s jejich počtem, takže mezera v logu není tichá.
/// </para>
/// </summary>
[ProviderAlias("JsonFile")]
public sealed class JsonFileLoggerProvider : ILoggerProvider
{
    private const string Category = "UAI.ZeroVP.API.Logging.JsonFileLoggerProvider";
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly FileLoggingOptions _options;
    private readonly string _directory;
    private readonly string _filePrefix;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<string> _queue;
    private readonly Task _writerTask;
    private int _disposed;
    private long _droppedCount;
    private long _unreportedDropCount;

    /// <summary>Vytvoří poskytovatele a spustí zapisovací vlákno.</summary>
    /// <param name="options">Nastavení souborového logu.</param>
    /// <param name="contentRootPath">Základ pro relativní cestu ke složce s logy.</param>
    /// <param name="timeProvider">Zdroj času; výchozí je systémový.</param>
    /// <param name="instanceId">
    /// ID instance. Do názvu souboru se promítne jen při <see cref="FileLoggingOptions.IncludeInstanceId"/>.
    /// </param>
    public JsonFileLoggerProvider(
        FileLoggingOptions options,
        string contentRootPath,
        TimeProvider? timeProvider = null,
        string? instanceId = null)
    {
        _options = options;
        _directory = Path.GetFullPath(options.Directory, contentRootPath);
        _filePrefix = LogFileNaming.BuildPrefix(options.FileNamePrefix, instanceId, options.IncludeInstanceId);
        _timeProvider = timeProvider ?? TimeProvider.System;

        // DropWrite: při plné frontě se nový záznam zahodí a TryWrite přesto vrátí true.
        // Zahození je proto vidět jen přes zpětné volání itemDropped, kde se počítá.
        _queue = Channel.CreateBounded<string>(
            new BoundedChannelOptions(Math.Max(1, options.QueueCapacity))
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropWrite
            },
            itemDropped: _ => OnItemDropped());

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

        var finished = true;

        try
        {
            finished = _writerTask.Wait(ShutdownTimeout);
        }
        catch (AggregateException)
        {
            // Chyba zapisovače už byla ohlášena na stderr.
        }

        if (!finished)
        {
            Console.Error.WriteLine(
                $"[JsonFileLogger] Zápis zbytku fronty nedoběhl do {ShutdownTimeout.TotalSeconds:0} s; záznamy, které v ní zůstaly, se mohly ztratit.");
        }

        // Zahozené záznamy, které už zapisovač nestihl ohlásit do souboru.
        var unreported = Interlocked.Exchange(ref _unreportedDropCount, 0);
        if (unreported > 0)
        {
            Console.Error.WriteLine(DropMessage(unreported));
        }
    }

    /// <summary>Kolik záznamů se celkem zahodilo kvůli plné frontě.</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    internal TimeProvider Clock => _timeProvider;

    // False vrací jen uzavřená fronta po Dispose (konec aplikace), ne plná fronta.
    internal void Enqueue(string line) => _queue.Writer.TryWrite(line);

    private void OnItemDropped()
    {
        Interlocked.Increment(ref _droppedCount);
        Interlocked.Increment(ref _unreportedDropCount);
    }

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

                    ReportDrops(writer);

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
            try
            {
                ReportDrops(writer);
                writer?.Flush();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }

            writer?.Dispose();
        }
    }

    // Zapíše do souboru (a na stderr) varování o záznamech zahozených od posledního hlášení.
    // Bez otevřeného souboru počet zůstane čekat na další dávku, nebo ho ohlásí Dispose na stderr.
    private void ReportDrops(StreamWriter? writer)
    {
        if (writer is null || Interlocked.Read(ref _unreportedDropCount) == 0)
        {
            return;
        }

        var dropped = Interlocked.Exchange(ref _unreportedDropCount, 0);
        if (dropped == 0)
        {
            return;
        }

        var message = DropMessage(dropped);
        var state = new StructuredLogState(message).With("DroppedCount", dropped);

        writer.WriteLine(JsonLogEntryFormatter.Format(
            _timeProvider.GetUtcNow(),
            Category,
            LogLevel.Warning,
            LogEvents.LogEntriesDropped,
            state,
            exception: null,
            message));

        Console.Error.WriteLine(message);
    }

    private static string DropMessage(long dropped) => string.Create(
        CultureInfo.InvariantCulture,
        $"[JsonFileLogger] Fronta souborového logu byla plná, zahozeno {dropped} záznamů.");

    private string GetFilePath()
    {
        return Path.Combine(_directory, LogFileNaming.BuildFileName(_filePrefix, _timeProvider.GetUtcNow()));
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
            // Filtr podle přesného tvaru zaručí, že instance nesmaže logy jiné instance
            // (maska "zerovp-api-*.log" by chytila i "zerovp-api-2-20261007.log").
            var obsolete = new DirectoryInfo(_directory)
                .GetFiles($"{_filePrefix}-*.log")
                .Where(file => LogFileNaming.IsLogFileName(file.Name, _filePrefix))
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
