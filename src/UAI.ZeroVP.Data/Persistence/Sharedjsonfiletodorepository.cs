using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;

namespace UAI.ZeroVP.Data.Persistence;

/// <summary>
/// Úložiště úkolů, jehož jediným zdrojem pravdy je JSON soubor sdílený několika instancemi API
/// (i několika procesy). Každá operace proběhne takto:
/// <list type="number">
/// <item>zamkne se soubor <c>{soubor}.lock</c> (v rámci procesu navíc <see cref="SemaphoreSlim"/>),</item>
/// <item>aktuální obsah se přečte ze souboru,</item>
/// <item>operace se provede a změna se zapíše do dočasného souboru, který se atomicky přesune na místo původního.</item>
/// </list>
/// Díky tomu je úkol vytvořený jednou instancí hned vidět všem ostatním, současné zápisy se serializují
/// (neztratí se ani nepoškodí data) a pád uprostřed zápisu nezanechá napůl zapsaný soubor.
/// Nevyžaduje <see cref="LoadAsync"/> ani <see cref="SaveAsync"/> pro správnou funkci; ukládání při ukončení
/// aplikace není potřeba, protože se zapisuje po každé změně.
/// </summary>
public sealed class SharedJsonFileTodoRepository : ITodoRepository
{
    private const int MoveAttempts = 5;

    private readonly string _filePath;
    private readonly string _lockPath;
    private readonly JsonPersistenceOptions _options;
    private readonly InMemoryTodoOptions _todoOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SharedJsonFileTodoRepository> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _reportedCorruptionStamp;

    /// <summary>Vytvoří repozitář nad souborem z <see cref="JsonPersistenceOptions.FilePath"/>.</summary>
    public SharedJsonFileTodoRepository(
        JsonPersistenceOptions options,
        InMemoryTodoOptions? todoOptions = null,
        TimeProvider? timeProvider = null,
        ILogger<SharedJsonFileTodoRepository>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _todoOptions = todoOptions ?? new InMemoryTodoOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<SharedJsonFileTodoRepository>.Instance;
        _filePath = Path.GetFullPath(options.FilePath);
        _lockPath = _filePath + ".lock";
    }

    /// <summary>Úplná cesta ke sdílenému souboru.</summary>
    public string FilePath => _filePath;

    /// <inheritdoc />
    public Task<IReadOnlyList<TodoItem>> GetAllAsync(bool? isCompleted = null, string? userId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyList<TodoItem>>(
            items =>
            {
                var result = items.Values
                    .Where(item => isCompleted is null || item.IsCompleted == isCompleted)
                    .Where(item => userId is null || string.Equals(item.UserId, userId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(item => item.Priority)
                    .ThenBy(item => item.CreatedAtUtc)
                    .ThenBy(item => item.DueAtUtc)
                    .Select(item => item.Clone())
                    .ToList();

                return (result, false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<TodoItem?>(
            items => (items.TryGetValue(id, out var item) ? item.Clone() : null, false),
            cancellationToken);

    /// <inheritdoc />
    public Task<TodoItem> AddAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var stored = item.Clone();

        return ExecuteAsync(
            items =>
            {
                if (!items.TryAdd(stored.Id, stored))
                {
                    throw new InvalidOperationException($"Úkol s identifikátorem {stored.Id} již existuje.");
                }

                return (stored.Clone(), true);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TodoItem>> AddRangeAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var toStore = items
            .Select(item => (item ?? throw new ArgumentException("The batch must not contain null items.", nameof(items))).Clone())
            .ToList();

        return ExecuteAsync<IReadOnlyList<TodoItem>>(
            existing =>
            {
                // Dávka je atomická: nejdřív se zkontrolují všechna ID, teprve potom se cokoli přidá.
                var seen = new HashSet<Guid>();

                foreach (var item in toStore)
                {
                    if (existing.ContainsKey(item.Id) || !seen.Add(item.Id))
                    {
                        throw new InvalidOperationException($"A task with id {item.Id} already exists.");
                    }
                }

                foreach (var item in toStore)
                {
                    existing[item.Id] = item;
                }

                return (toStore.Select(item => item.Clone()).ToList(), true);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var replacement = item.Clone();

        return ExecuteAsync(
            items =>
            {
                if (!items.ContainsKey(replacement.Id))
                {
                    return (false, false);
                }

                items[replacement.Id] = replacement;

                return (true, true);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            items =>
            {
                var removed = items.Remove(id);

                return (removed, removed);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(items => (items.Count, false), cancellationToken);

    /// <summary>
    /// Připraví sdílený soubor při startu instance: neexistuje-li, vytvoří ho s ukázkovými daty
    /// (pokud je <see cref="InMemoryTodoOptions.SeedSampleData"/> zapnuté). Při souběžném startu
    /// více instancí to udělá jen první, ostatní soubor najdou hotový.
    /// </summary>
    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            items =>
            {
                if (File.Exists(_filePath))
                {
                    _logger.LogInformation("Shared task storage '{FilePath}' is in use ({Count} task(s)).", _filePath, items.Count);

                    return (items.Count, false);
                }

                if (!_todoOptions.SeedSampleData)
                {
                    _logger.LogInformation("Shared task storage file '{FilePath}' was not found. Starting with an empty state.", _filePath);

                    return (0, false);
                }

                foreach (var sample in TodoSampleData.Create(_timeProvider.GetUtcNow()))
                {
                    items[sample.Id] = sample;
                }

                _logger.LogInformation("Shared task storage file '{FilePath}' was not found. Created it with {Count} sample task(s).", _filePath, items.Count);

                return (items.Count, true);
            },
            cancellationToken);

    /// <summary>Nic nedělá: sdílený režim zapisuje každou změnu okamžitě.</summary>
    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private async Task<T> ExecuteAsync<T>(
        Func<Dictionary<Guid, TodoItem>, (T Result, bool Changed)> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var fileLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);

            var items = ReadItems();
            var (result, changed) = operation(items);

            if (changed)
            {
                WriteItems(items.Values);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Získá výhradní zámek přes soubor <c>.lock</c> (<see cref="FileShare.None"/>). Když ho drží jiný proces,
    /// opakuje pokus s rostoucí prodlevou, nejdéle <see cref="JsonPersistenceOptions.LockTimeoutMilliseconds"/>.
    /// </summary>
    private async Task<FileStream> AcquireFileLockAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        var timeout = TimeSpan.FromMilliseconds(_options.LockTimeoutMilliseconds);
        var waited = Stopwatch.StartNew();
        var delayMilliseconds = 2;

        while (true)
        {
            try
            {
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (waited.Elapsed >= timeout)
                {
                    throw new TimeoutException(
                        $"Timed out after {_options.LockTimeoutMilliseconds} ms waiting for exclusive access to '{_lockPath}'.",
                        exception);
                }
            }

            await Task.Delay(delayMilliseconds + RandomNumberGenerator.GetInt32(0, 5), cancellationToken).ConfigureAwait(false);
            delayMilliseconds = Math.Min(delayMilliseconds * 2, 50);
        }
    }

    private Dictionary<Guid, TodoItem> ReadItems()
    {
        var items = new Dictionary<Guid, TodoItem>();

        if (!File.Exists(_filePath))
        {
            return items;
        }

        var json = File.ReadAllText(_filePath, Encoding.UTF8);

        if (string.IsNullOrWhiteSpace(json))
        {
            return items;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<List<TodoItem>>(json, TodoJson.Options);

            foreach (var item in loaded ?? [])
            {
                if (item is not null)
                {
                    items[item.Id] = item;
                }
            }
        }
        catch (JsonException exception)
        {
            ReportCorruption(exception);
            items.Clear();
        }

        return items;
    }

    private void ReportCorruption(JsonException exception)
    {
        var info = new FileInfo(_filePath);
        var stamp = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";

        // Poškozený soubor se hlásí a zálohuje jen jednou, ne při každém čtení.
        if (_reportedCorruptionStamp == stamp)
        {
            return;
        }

        _reportedCorruptionStamp = stamp;

        var backup = $"{_filePath}.corrupt-{_timeProvider.GetUtcNow():yyyyMMddHHmmssfff}";

        try
        {
            File.Copy(_filePath, backup, overwrite: true);
        }
        catch (Exception copyException) when (copyException is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(copyException, "Could not back up the corrupted file '{FilePath}'.", _filePath);
            backup = "(backup failed)";
        }

        _logger.LogError(
            exception,
            "Shared storage file '{FilePath}' contains corrupted JSON. Starting with an empty state; a copy was saved to '{BackupPath}'.",
            _filePath,
            backup);
    }

    private void WriteItems(IEnumerable<TodoItem> items)
    {
        var json = JsonSerializer.Serialize(
            items.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).ToList(),
            TodoJson.Options);

        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    // Atomická výměna: čtenář uvidí buď starý, nebo nový soubor, nikdy napůl zapsaný.
                    File.Move(temporaryPath, _filePath, overwrite: true);

                    return;
                }
                catch (Exception exception) when (attempt < MoveAttempts && (exception is IOException or UnauthorizedAccessException))
                {
                    // Antivir nebo indexer mohou soubor na chvíli držet (hlavně ve Windows).
                    Thread.Sleep(20 * attempt);
                }
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}