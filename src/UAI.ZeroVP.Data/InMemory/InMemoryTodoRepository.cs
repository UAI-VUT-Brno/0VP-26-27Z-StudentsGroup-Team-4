using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.Persistence;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Data.InMemory;

/// <summary>
/// Úložiště úkolů v operační paměti procesu s volitelnou persistencí do JSON souboru.
/// </summary>
public sealed class InMemoryTodoRepository : ITodoRepository
{
    private readonly ConcurrentDictionary<Guid, TodoItem> _items = new();
    private readonly TimeProvider _timeProvider;
    private readonly IFileStorage _fileStorage;
    private readonly JsonPersistenceOptions _persistenceOptions;
    private readonly ILogger<InMemoryTodoRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Inicializuje instanci repozitáře.
    /// </summary>
    public InMemoryTodoRepository(
        InMemoryTodoOptions? options = null,
        TimeProvider? timeProvider = null,
        IFileStorage? fileStorage = null,
        JsonPersistenceOptions? persistenceOptions = null,
        ILogger<InMemoryTodoRepository>? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _fileStorage = fileStorage ?? new PhysicalFileStorage();
        _persistenceOptions = persistenceOptions ?? new JsonPersistenceOptions { Enabled = false };
        _logger = logger ?? NullLogger<InMemoryTodoRepository>.Instance;

        if (options?.SeedSampleData ?? true)
        {
            Seed();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TodoItem>> GetAllAsync(bool? isCompleted = null, string? userId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<TodoItem> result = _items.Values
            .Where(item => isCompleted is null || item.IsCompleted == isCompleted)
            .Where(item => userId is null || string.Equals(item.UserId, userId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.DueAtUtc)
            .Select(item => item.Clone())
            .ToList();

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_items.TryGetValue(id, out var item) ? item.Clone() : null);
    }

    /// <inheritdoc />
    public Task<TodoItem> AddAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        var stored = item.Clone();
        if (!_items.TryAdd(stored.Id, stored))
        {
            throw new InvalidOperationException($"Úkol s identifikátorem {stored.Id} již existuje.");
        }

        return Task.FromResult(stored.Clone());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TodoItem>> AddRangeAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();

        var toStore = items
            .Select(item => (item ?? throw new ArgumentException("The batch must not contain null items.", nameof(items))).Clone())
            .ToList();

        var added = new List<TodoItem>(toStore.Count);
        foreach (var item in toStore)
        {
            if (!_items.TryAdd(item.Id, item))
            {
                foreach (var addedItem in added)
                {
                    _items.TryRemove(new KeyValuePair<Guid, TodoItem>(addedItem.Id, addedItem));
                }

                throw new InvalidOperationException($"A task with id {item.Id} already exists.");
            }

            added.Add(item);
        }

        IReadOnlyList<TodoItem> result = toStore.Select(item => item.Clone()).ToList();

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        var replacement = item.Clone();

        while (_items.TryGetValue(item.Id, out var existing))
        {
            if (_items.TryUpdate(item.Id, replacement, existing))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_items.TryRemove(id, out _));
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_items.Count);
    }

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!_persistenceOptions.Enabled)
        {
            return;
        }

        var path = _persistenceOptions.FilePath;

        if (!_fileStorage.FileExists(path))
        {
            _logger.LogInformation("Persistent task storage file '{FilePath}' was not found. Initializing with empty state.", path);
            return;
        }

        try
        {
            var json = await _fileStorage.ReadAllTextAsync(path, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("Persistent task storage file '{FilePath}' is empty. Initializing with empty state.", path);
                _items.Clear();
                return;
            }

            var loadedTasks = JsonSerializer.Deserialize<List<TodoItem>>(json, JsonOptions);
            _items.Clear();

            if (loadedTasks != null)
            {
                foreach (var task in loadedTasks)
                {
                    _items[task.Id] = task;
                }
            }

            _logger.LogInformation("Successfully initialized {Count} task(s) from persistent storage '{FilePath}'.", _items.Count, path);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Persistent storage file '{FilePath}' contains corrupted JSON. Starting with an empty state.", path);
            _items.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred while loading persistent storage file '{FilePath}'. Starting empty.", path);
            _items.Clear();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!_persistenceOptions.Enabled)
        {
            return;
        }

        var path = _persistenceOptions.FilePath;

        try
        {
            var taskList = _items.Values
                .OrderBy(i => i.CreatedAtUtc)
                .ToList();

            var json = JsonSerializer.Serialize(taskList, JsonOptions);
            await _fileStorage.WriteAllTextAsync(path, json, cancellationToken);

            _logger.LogInformation("Successfully saved {Count} task(s) to persistent storage '{FilePath}'.", taskList.Count, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save tasks to persistent storage file '{FilePath}'.", path);
            throw;
        }
    }

    private void Seed()
    {
        var now = _timeProvider.GetUtcNow();

        AddSeedItem("Připravit ukázkové API", "Tři projekty: DTO, Data, API.", TodoPriority.High, isCompleted: true, now.AddDays(-2), userId: "anna");
        AddSeedItem("Napsat Dockerfile", "Vícefázový build nad SDK a runtime image.", TodoPriority.High, isCompleted: false, now.AddDays(-1), userId: "anna");
        AddSeedItem("Vyzkoušet docker compose", "Spustit API ve dvou replikách.", TodoPriority.Normal, isCompleted: false, now.AddHours(-3), userId: "petr");
        AddSeedItem("Uklidit nepoužité image", "docker image prune", TodoPriority.Low, isCompleted: false, now.AddMinutes(-30), userId: "petr");
    }

    private void AddSeedItem(string title, string description, TodoPriority priority, bool isCompleted, DateTimeOffset createdAtUtc, string? userId = null)
    {
        var item = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            Priority = priority,
            IsCompleted = isCompleted,
            CreatedAtUtc = createdAtUtc,
            CompletedAtUtc = isCompleted ? createdAtUtc.AddHours(1) : null,
            UpdatedAtUtc = createdAtUtc,
            UserId = userId
        };

        _items[item.Id] = item;
    }
}