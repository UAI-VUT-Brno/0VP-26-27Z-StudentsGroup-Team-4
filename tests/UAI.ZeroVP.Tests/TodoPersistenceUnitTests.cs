using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.API.HostedServices;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.Data.Persistence;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class TodoPersistenceUnitTests
{
    private sealed class FakeFileStorage : IFileStorage
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? LastWrittenPath { get; private set; }
        public string? LastWrittenContent { get; private set; }
        public int WriteCount { get; private set; }

        public bool FileExists(string path) => Files.ContainsKey(path);

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            if (Files.TryGetValue(path, out var content))
            {
                return Task.FromResult(content);
            }
            throw new FileNotFoundException("File not found", path);
        }

        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            Files[path] = content;
            LastWrittenPath = path;
            LastWrittenContent = content;
            WriteCount++;
            return Task.CompletedTask;
        }
    }

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

    private sealed class TestApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stoppingCts = new();
        private readonly CancellationTokenSource _stoppedCts = new();
        private readonly CancellationTokenSource _startedCts = new();

        public CancellationToken ApplicationStarted => _startedCts.Token;
        public CancellationToken ApplicationStopping => _stoppingCts.Token;
        public CancellationToken ApplicationStopped => _stoppedCts.Token;

        public void StopApplication() => _stoppingCts.Cancel();
    }

    [Fact]
    public void Serialization_And_Deserialization_ShouldPreserveDataAccurately()
    {
        var originalTasks = new List<TodoItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Preserved Task 1",
                Description = "Description 1",
                Priority = TodoPriority.High,
                IsCompleted = true,
                CreatedAtUtc = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero),
                CompletedAtUtc = new DateTimeOffset(2026, 1, 10, 11, 0, 0, TimeSpan.Zero),
                UserId = "anna"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Preserved Task 2",
                Description = "Description 2",
                Priority = TodoPriority.Normal,
                IsCompleted = false,
                CreatedAtUtc = new DateTimeOffset(2026, 1, 11, 10, 0, 0, TimeSpan.Zero),
                UserId = "petr"
            }
        };

        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };

        var json = JsonSerializer.Serialize(originalTasks, options);
        var deserialized = JsonSerializer.Deserialize<List<TodoItem>>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(originalTasks.Count, deserialized.Count);
        for (int i = 0; i < originalTasks.Count; i++)
        {
            Assert.Equal(originalTasks[i].Id, deserialized[i].Id);
            Assert.Equal(originalTasks[i].Title, deserialized[i].Title);
            Assert.Equal(originalTasks[i].Description, deserialized[i].Description);
            Assert.Equal(originalTasks[i].Priority, deserialized[i].Priority);
            Assert.Equal(originalTasks[i].IsCompleted, deserialized[i].IsCompleted);
            Assert.Equal(originalTasks[i].UserId, deserialized[i].UserId);
            Assert.Equal(originalTasks[i].CreatedAtUtc, deserialized[i].CreatedAtUtc);
            Assert.Equal(originalTasks[i].CompletedAtUtc, deserialized[i].CompletedAtUtc);
        }
    }

    [Fact]
    public async Task LoadAsync_WithValidJson_ShouldPopulateRepositoryState()
    {
        var fakeStorage = new FakeFileStorage();
        var persistenceOptions = new JsonPersistenceOptions { Enabled = true, FilePath = "test/todos.json" };
        var logger = new TestLogger<InMemoryTodoRepository>();

        var expectedId = Guid.NewGuid();
        var validJson = @"[
            {
                ""Id"": """ + expectedId + @""",
                ""Title"": ""Persisted Item"",
                ""Description"": ""From JSON"",
                ""Priority"": ""High"",
                ""IsCompleted"": false,
                ""CreatedAtUtc"": ""2026-01-01T12:00:00Z"",
                ""UserId"": ""test-user""
            }
        ]";

        fakeStorage.Files[persistenceOptions.FilePath] = validJson;

        var repository = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = false },
            TimeProvider.System,
            fakeStorage,
            persistenceOptions,
            logger);

        await repository.LoadAsync();

        var tasks = await repository.GetAllAsync();
        Assert.Single(tasks);
        Assert.Equal(expectedId, tasks[0].Id);
        Assert.Equal("Persisted Item", tasks[0].Title);
        Assert.Equal("test-user", tasks[0].UserId);
    }

    [Fact]
    public async Task LoadAsync_WithCorruptedJson_ShouldLogErrorAndInitializeEmpty()
    {
        var fakeStorage = new FakeFileStorage();
        var persistenceOptions = new JsonPersistenceOptions { Enabled = true, FilePath = "test/corrupted.json" };
        var logger = new TestLogger<InMemoryTodoRepository>();

        fakeStorage.Files[persistenceOptions.FilePath] = "{ corrupted invalid json content [[[";

        var repository = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = false },
            TimeProvider.System,
            fakeStorage,
            persistenceOptions,
            logger);

        await repository.LoadAsync();

        Assert.Equal(0, await repository.CountAsync());
        Assert.Contains(logger.Logs, l => l.Level == LogLevel.Error && l.Message.Contains("corrupted"));
    }

    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ShouldInitializeEmptyWithoutException()
    {
        var fakeStorage = new FakeFileStorage();
        var persistenceOptions = new JsonPersistenceOptions { Enabled = true, FilePath = "test/nonexistent.json" };
        var logger = new TestLogger<InMemoryTodoRepository>();

        var repository = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = false },
            TimeProvider.System,
            fakeStorage,
            persistenceOptions,
            logger);

        var ex = await Record.ExceptionAsync(() => repository.LoadAsync());

        Assert.Null(ex);
        Assert.Equal(0, await repository.CountAsync());
        Assert.Contains(logger.Logs, l => l.Level == LogLevel.Information && l.Message.Contains("was not found"));
    }

    [Fact]
    public async Task Shutdown_ShouldTriggerSaveWithCurrentTasks()
    {
        var fakeStorage = new FakeFileStorage();
        var persistenceOptions = new JsonPersistenceOptions { Enabled = true, FilePath = "test/shutdown.json" };
        var repoLogger = new TestLogger<InMemoryTodoRepository>();
        var serviceLogger = new TestLogger<TodoPersistenceHostedService>();

        var repository = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = false },
            TimeProvider.System,
            fakeStorage,
            persistenceOptions,
            repoLogger);

        var lifetime = new TestApplicationLifetime();
        var hostedService = new TodoPersistenceHostedService(repository, lifetime, serviceLogger);

        await hostedService.StartAsync(CancellationToken.None);

        await repository.AddAsync(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Task Before Shutdown",
            Priority = TodoPriority.Normal,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        lifetime.StopApplication();

        Assert.Equal(1, fakeStorage.WriteCount);
        Assert.Equal(persistenceOptions.FilePath, fakeStorage.LastWrittenPath);
        Assert.Contains("Task Before Shutdown", fakeStorage.LastWrittenContent);
    }
}