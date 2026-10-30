using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.Data.Persistence;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public sealed class TodoPersistenceIntegrationTests : IDisposable
{
    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Logs { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Logs.Add((logLevel, formatter(state, exception), exception));
        }
    }

    private readonly string _tempDirectory;
    private readonly string _tempFilePath;
    private readonly PhysicalFileStorage _storage;
    private readonly JsonPersistenceOptions _options;

    public TodoPersistenceIntegrationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "zerovp_integration_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _tempFilePath = Path.Combine(_tempDirectory, "todos.json");

        _storage = new PhysicalFileStorage();
        _options = new JsonPersistenceOptions
        {
            Enabled = true,
            FilePath = _tempFilePath
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task EndToEnd_Persistence_ShouldSaveAndReloadTasksAcrossInstances()
    {
        var repo1 = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = false },
            TimeProvider.System,
            _storage,
            _options,
            NullLogger<InMemoryTodoRepository>.Instance);

        var task1 = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Integration Task 1",
            Description = "E2E persistence check",
            Priority = TodoPriority.High,
            IsCompleted = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UserId = "integration-user"
        };
        var task2 = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Integration Task 2",
            Description = "Completed check",
            Priority = TodoPriority.Low,
            IsCompleted = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await repo1.AddAsync(task1);
        await repo1.AddAsync(task2);
        await repo1.SaveAsync();

        Assert.True(File.Exists(_tempFilePath));

        var repo2 = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = false },
            TimeProvider.System,
            _storage,
            _options,
            NullLogger<InMemoryTodoRepository>.Instance);

        await repo2.LoadAsync();
        var reloaded = await repo2.GetAllAsync();

        Assert.Equal(2, reloaded.Count);
        Assert.Contains(reloaded, t => t.Id == task1.Id && t.Title == "Integration Task 1" && t.UserId == "integration-user");
        Assert.Contains(reloaded, t => t.Id == task2.Id && t.Title == "Integration Task 2" && t.IsCompleted);
    }

    [Fact]
    public async Task EdgeCase_PhysicallyCorruptedFile_ShouldNotCrashAndStartEmpty()
    {
        await File.WriteAllTextAsync(_tempFilePath, "<<<corrupted raw data that is not valid json>>>");
        var logger = new TestLogger<InMemoryTodoRepository>();

        var repo = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = false },
            TimeProvider.System,
            _storage,
            _options,
            logger);

        var ex = await Record.ExceptionAsync(() => repo.LoadAsync());

        Assert.Null(ex);
        Assert.Equal(0, await repo.CountAsync());
        Assert.Contains(logger.Logs, l => l.Level == LogLevel.Error && l.Message.Contains("corrupted"));
    }

    [Fact]
    public async Task EdgeCase_FileDoesNotExistInitially_ShouldBeCreatedUponFirstSave()
    {
        Assert.False(File.Exists(_tempFilePath));

        var repo = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = false },
            TimeProvider.System,
            _storage,
            _options,
            NullLogger<InMemoryTodoRepository>.Instance);

        await repo.LoadAsync();

        await repo.AddAsync(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Created on initial save",
            Priority = TodoPriority.Normal,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await repo.SaveAsync();

        Assert.True(File.Exists(_tempFilePath));
        var content = await File.ReadAllTextAsync(_tempFilePath);
        Assert.Contains("Created on initial save", content);
    }
}