using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.Data.Persistence;
using UAI.ZeroVP.Tests.Logging;

namespace UAI.ZeroVP.Tests;

/// <summary>
/// Shared JSON file storage: several repository instances (standing in for several API instances)
/// work on one file. Every change must be visible to the others and concurrent writes must not lose
/// or corrupt data.
/// </summary>
public sealed class SharedJsonFileTodoRepositoryTests : IDisposable
{
    private readonly TempDir _temp = new();

    private string FilePath => Path.Combine(_temp.FullPath, "data", "todos.json");

    public void Dispose() => _temp.Dispose();

    private SharedJsonFileTodoRepository Create(
        bool seed = false,
        ILogger<SharedJsonFileTodoRepository>? logger = null,
        int lockTimeoutMilliseconds = 10_000) =>
        new(
            new JsonPersistenceOptions
            {
                Enabled = true,
                Shared = true,
                FilePath = FilePath,
                LockTimeoutMilliseconds = lockTimeoutMilliseconds
            },
            new InMemoryTodoOptions { SeedSampleData = seed },
            TimeProvider.System,
            logger);

    private static TodoItem NewItem(string title, string? userId = null, bool isCompleted = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UserId = userId,
            IsCompleted = isCompleted
        };

    private int TasksInFile() => JsonDocument.Parse(File.ReadAllText(FilePath)).RootElement.GetArrayLength();

    [Fact]
    public async Task TaskAddedThroughOneRepository_IsVisibleThroughAnother()
    {
        var a = Create();
        var b = Create();
        var item = NewItem("created on A");

        await a.AddAsync(item);

        var found = await b.GetByIdAsync(item.Id);
        Assert.NotNull(found);
        Assert.Equal("created on A", found!.Title);
        Assert.Equal(1, await b.CountAsync());
    }

    [Fact]
    public async Task UpdateAndDelete_AreVisibleThroughAnotherRepository()
    {
        var a = Create();
        var b = Create();
        var item = NewItem("original");
        await a.AddAsync(item);

        var changed = item.Clone();
        changed.Title = "changed on A";
        changed.IsCompleted = true;
        Assert.True(await a.UpdateAsync(changed));

        var afterUpdate = await b.GetByIdAsync(item.Id);
        Assert.Equal("changed on A", afterUpdate!.Title);
        Assert.True(afterUpdate.IsCompleted);

        Assert.True(await b.DeleteAsync(item.Id));
        Assert.Null(await a.GetByIdAsync(item.Id));
        Assert.Equal(0, await a.CountAsync());
    }

    [Fact]
    public async Task UpdateAndDelete_OfUnknownTask_ReturnFalse()
    {
        var repository = Create();

        Assert.False(await repository.UpdateAsync(NewItem("ghost")));
        Assert.False(await repository.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAll_FiltersByCompletionAndUser_LikeTheInMemoryRepository()
    {
        var repository = Create();
        await repository.AddAsync(NewItem("anna open", "anna"));
        await repository.AddAsync(NewItem("anna done", "ANNA", isCompleted: true));
        await repository.AddAsync(NewItem("petr open", "petr"));

        Assert.Equal(3, (await repository.GetAllAsync()).Count);
        Assert.Equal(2, (await repository.GetAllAsync(userId: "anna")).Count);
        Assert.Single(await repository.GetAllAsync(isCompleted: true, userId: "anna"));
        Assert.Equal(2, (await repository.GetAllAsync(isCompleted: false)).Count);
    }

    [Fact]
    public async Task Add_WithExistingId_Throws_AndLeavesTheFileUnchanged()
    {
        var repository = Create();
        var item = NewItem("one");
        await repository.AddAsync(item);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddAsync(item));

        Assert.Equal(1, TasksInFile());
    }

    [Fact]
    public async Task AddRange_IsAtomic_WhenOneIdAlreadyExists()
    {
        var repository = Create();
        var existing = NewItem("existing");
        await repository.AddAsync(existing);

        var batch = new[] { NewItem("new 1"), existing, NewItem("new 2") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddRangeAsync(batch));

        Assert.Equal(1, await repository.CountAsync());
        Assert.Equal(1, TasksInFile());
    }

    [Fact]
    public async Task AddRange_StoresTheWholeBatch()
    {
        var a = Create();
        var b = Create();

        var stored = await a.AddRangeAsync(new[] { NewItem("1"), NewItem("2"), NewItem("3") });

        Assert.Equal(3, stored.Count);
        Assert.Equal(3, await b.CountAsync());
    }

    [Fact]
    public async Task MissingFile_StartsEmpty_AndReadingDoesNotCreateIt()
    {
        var repository = Create();

        Assert.Equal(0, await repository.CountAsync());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task Load_WhenFileIsMissing_AndSeedingIsOn_CreatesSeededFile()
    {
        var repository = Create(seed: true);

        await repository.LoadAsync();

        Assert.True(File.Exists(FilePath));
        Assert.Equal(4, await repository.CountAsync());
    }

    [Fact]
    public async Task Load_ByManyInstancesStartingAtOnce_SeedsOnlyOnce()
    {
        var repositories = Enumerable.Range(0, 5).Select(_ => Create(seed: true)).ToList();

        await Task.WhenAll(repositories.Select(repository => Task.Run(() => repository.LoadAsync())));

        foreach (var repository in repositories)
        {
            Assert.Equal(4, await repository.CountAsync());
        }

        Assert.Equal(4, TasksInFile());
    }

    [Fact]
    public async Task Load_WhenFileIsMissing_AndSeedingIsOff_StartsEmpty()
    {
        var repository = Create(seed: false);

        await repository.LoadAsync();

        Assert.Equal(0, await repository.CountAsync());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task Load_WhenFileExists_KeepsItsContent_AndDoesNotSeed()
    {
        await Create().AddAsync(NewItem("existing"));

        var second = Create(seed: true);
        await second.LoadAsync();

        Assert.Equal(1, await second.CountAsync());
    }

    [Fact]
    public async Task EmptyFile_IsTreatedAsNoTasks()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, "   ");

        Assert.Equal(0, await Create().CountAsync());
    }

    [Fact]
    public async Task CorruptedFile_StartsEmpty_LogsOnce_KeepsABackup_AndRecoversOnNextWrite()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, "<<<not json>>>");
        var logger = new ListLogger();
        var repository = Create(logger: logger);

        Assert.Equal(0, await repository.CountAsync());
        Assert.Equal(0, await repository.CountAsync());

        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("corrupted", error.Message);

        var backup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!, "todos.json.corrupt-*"));
        Assert.Equal("<<<not json>>>", await File.ReadAllTextAsync(backup));

        await repository.AddAsync(NewItem("after corruption"));

        Assert.Equal(1, TasksInFile());
        Assert.Equal(1, await Create().CountAsync());
    }

    [Fact]
    public async Task ConcurrentWritesFromManyRepositories_LoseNothing_AndLeaveValidJson()
    {
        const int repositoryCount = 6;
        const int writesPerRepository = 20;

        var ids = new ConcurrentBag<Guid>();
        var repositories = Enumerable.Range(0, repositoryCount).Select(_ => Create()).ToList();

        await Task.WhenAll(repositories.Select(repository => Task.Run(async () =>
        {
            for (var i = 0; i < writesPerRepository; i++)
            {
                var item = NewItem($"task-{i}");
                await repository.AddAsync(item);
                ids.Add(item.Id);
            }
        })));

        var all = await Create().GetAllAsync();

        Assert.Equal(repositoryCount * writesPerRepository, all.Count);
        Assert.Equal(ids.Count, all.Select(item => item.Id).Distinct().Count());
        Assert.Equal(repositoryCount * writesPerRepository, TasksInFile());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(FilePath)!, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentReadModifyWrite_OnTheSameTask_NeverCorruptsTheFile()
    {
        var seed = Create();
        var item = NewItem("contended");
        await seed.AddAsync(item);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(n => Task.Run(async () =>
        {
            var repository = Create();

            for (var i = 0; i < 15; i++)
            {
                var copy = (await repository.GetByIdAsync(item.Id))!;
                copy.Title = $"writer-{n}-{i}";
                Assert.True(await repository.UpdateAsync(copy));
            }
        })));

        Assert.Equal(1, TasksInFile());
        Assert.StartsWith("writer-", (await Create().GetByIdAsync(item.Id))!.Title);
    }

    [Fact]
    public async Task WhenAnotherProcessHoldsTheLock_TheOperationTimesOutInsteadOfHanging()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        using var held = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var repository = Create(lockTimeoutMilliseconds: 150);

        await Assert.ThrowsAsync<TimeoutException>(() => repository.CountAsync());
    }

    [Fact]
    public async Task LockIsReleased_AfterEveryOperation_EvenWhenItFails()
    {
        var repository = Create();
        var item = NewItem("one");
        await repository.AddAsync(item);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddAsync(item));

        // If the lock leaked, opening the lock file exclusively would fail.
        using var probe = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.NotNull(probe);
    }

    [Fact]
    public async Task Save_IsANoOp_BecauseEveryChangeIsWrittenImmediately()
    {
        var repository = Create();

        await repository.SaveAsync();

        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task FileFormat_IsReadableByTheSnapshotRepository()
    {
        var item = NewItem("portable", "anna");
        await Create().AddAsync(item);

        var snapshot = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = false },
            TimeProvider.System,
            new PhysicalFileStorage(),
            new JsonPersistenceOptions { Enabled = true, FilePath = FilePath });
        await snapshot.LoadAsync();

        var loaded = Assert.Single(await snapshot.GetAllAsync());
        Assert.Equal(item.Id, loaded.Id);
        Assert.Equal("anna", loaded.UserId);
    }

    private sealed class ListLogger : ILogger<SharedJsonFileTodoRepository>
    {
        private readonly object _lock = new();
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_lock)
                {
                    return _entries.ToList();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lock)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}