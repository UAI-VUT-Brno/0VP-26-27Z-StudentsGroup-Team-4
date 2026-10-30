# =====================================================================
# update-persistence.ps1
# Implements reviewer feedback for 0VP-T4-08 and 0VP-T4-12-2
# Run from repository root (where UAI.ZeroVP.sln is located)
# =====================================================================

$ErrorActionPreference = "Stop"

if (-not (Test-Path "UAI.ZeroVP.sln")) {
    Write-Error "Chyba: UAI.ZeroVP.sln nenalezen. Spustte tento skript v koreni repozitare."
    exit 1
}

Write-Host ">>> Aplikuji upravy podle review..." -ForegroundColor Cyan

function Write-Utf8File {
    param(
        [Parameter(Mandatory=$true)] [string]$Path,
        [Parameter(Mandatory=$true)] [string]$Content
    )
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.Encoding]::UTF8)
    Write-Host "  [+] $Path" -ForegroundColor Green
}

# ---------------------------------------------------------------------
# 1. .gitignore (Doplnìní data/)
# ---------------------------------------------------------------------

$gitIgnorePath = ".gitignore"
if (Test-Path $gitIgnorePath) {
    $giContent = [System.IO.File]::ReadAllText($gitIgnorePath, [System.Text.Encoding]::UTF8)
    if ($giContent -notmatch "(?m)^data/\s*$") {
        $giContent = $giContent.TrimEnd() + "`r`ndata/`r`n"
        [System.IO.File]::WriteAllText($gitIgnorePath, $giContent, [System.Text.Encoding]::UTF8)
        Write-Host "  [+] .gitignore (pridano data/)" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------
# 2. TodoPersistenceHostedService.cs (StopAsync + ApplicationStopping)
# ---------------------------------------------------------------------

Write-Utf8File "src/UAI.ZeroVP.API/HostedServices/TodoPersistenceHostedService.cs" @'
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.Data.Abstractions;

namespace UAI.ZeroVP.API.HostedServices;

/// <summary>
/// Hosted service that hooks into application startup and shutdown lifecycles
/// to initialize the task repository from file and save tasks before process termination.
/// </summary>
public sealed class TodoPersistenceHostedService : IHostedService
{
    private readonly ITodoRepository _repository;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<TodoPersistenceHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoPersistenceHostedService"/> class.
    /// </summary>
    /// <param name="repository">The task repository.</param>
    /// <param name="lifetime">The application lifetime.</param>
    /// <param name="logger">The logger instance.</param>
    public TodoPersistenceHostedService(
        ITodoRepository repository,
        IHostApplicationLifetime lifetime,
        ILogger<TodoPersistenceHostedService> logger)
    {
        _repository = repository;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing tasks from persistent storage on service startup...");
        await _repository.LoadAsync(cancellationToken);

        // We register an early save callback on ApplicationStopping to safeguard data
        // in case the shutdown sequence is cut short before reaching hosted services.
        _lifetime.ApplicationStopping.Register(OnApplicationStopping);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Final persistence flush:
        // We perform a save here in StopAsync because the HTTP server (Kestrel/TestServer) has already
        // stopped accepting new requests and drained all active in-flight requests. This ensures that
        // any requests completed during graceful shutdown are safely persisted.
        _logger.LogInformation("Application host is stopping. Persisting final tasks to storage in StopAsync...");
        await SaveSafeAsync(cancellationToken);
    }

    private void OnApplicationStopping()
    {
        // Early flush triggered when application shutdown begins.
        // We do not block subsequent saves so that StopAsync can capture in-flight request completions.
        _logger.LogInformation("Application stopping hook triggered. Performing early persistence flush...");
        SaveSafeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task SaveSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SaveAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while persisting tasks during application shutdown.");
        }
    }
}
'@

# ---------------------------------------------------------------------
# 3. TodoPersistenceUnitTests.cs (Zpøesnìné testy)
# ---------------------------------------------------------------------

Write-Utf8File "tests/UAI.ZeroVP.Tests/TodoPersistenceUnitTests.cs" @'
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.API.HostedServices;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.Data.Persistence;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class TodoPersistenceUnitTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

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
    public async Task Serialization_And_Deserialization_ShouldPreserveDataAccurately()
    {
        var fakeStorage = new FakeFileStorage();
        var persistenceOptions = new JsonPersistenceOptions { Enabled = true, FilePath = "test/todos.json" };
        var repoSave = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = false },
            TimeProvider.System,
            fakeStorage,
            persistenceOptions);

        var task1 = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Task 1",
            Description = "Full field check",
            Priority = TodoPriority.High,
            IsCompleted = true,
            CreatedAtUtc = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero),
            CompletedAtUtc = new DateTimeOffset(2026, 1, 10, 11, 0, 0, TimeSpan.Zero),
            DueAtUtc = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 1, 10, 11, 0, 0, TimeSpan.Zero),
            UserId = "anna"
        };
        var task2 = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Task 2",
            Description = "Nullable fields check",
            Priority = TodoPriority.Low,
            IsCompleted = false,
            CreatedAtUtc = new DateTimeOffset(2026, 1, 11, 8, 30, 0, TimeSpan.Zero),
            CompletedAtUtc = null,
            DueAtUtc = null,
            UpdatedAtUtc = new DateTimeOffset(2026, 1, 11, 8, 30, 0, TimeSpan.Zero),
            UserId = "petr"
        };

        await repoSave.AddAsync(task1);
        await repoSave.AddAsync(task2);
        await repoSave.SaveAsync();

        var repoLoad = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = false },
            TimeProvider.System,
            fakeStorage,
            persistenceOptions);

        await repoLoad.LoadAsync();
        var loaded = await repoLoad.GetAllAsync();

        Assert.Equal(2, loaded.Count);

        var loaded1 = loaded.Single(t => t.Id == task1.Id);
        Assert.Equal(task1.Title, loaded1.Title);
        Assert.Equal(task1.Description, loaded1.Description);
        Assert.Equal(task1.Priority, loaded1.Priority);
        Assert.Equal(task1.IsCompleted, loaded1.IsCompleted);
        Assert.Equal(task1.CreatedAtUtc, loaded1.CreatedAtUtc);
        Assert.Equal(task1.CompletedAtUtc, loaded1.CompletedAtUtc);
        Assert.Equal(task1.DueAtUtc, loaded1.DueAtUtc);
        Assert.Equal(task1.UpdatedAtUtc, loaded1.UpdatedAtUtc);
        Assert.Equal(task1.UserId, loaded1.UserId);

        var loaded2 = loaded.Single(t => t.Id == task2.Id);
        Assert.Equal(task2.Title, loaded2.Title);
        Assert.Equal(task2.Description, loaded2.Description);
        Assert.Equal(task2.Priority, loaded2.Priority);
        Assert.Equal(task2.IsCompleted, loaded2.IsCompleted);
        Assert.Equal(task2.CreatedAtUtc, loaded2.CreatedAtUtc);
        Assert.Equal(task2.CompletedAtUtc, loaded2.CompletedAtUtc);
        Assert.Equal(task2.DueAtUtc, loaded2.DueAtUtc);
        Assert.Equal(task2.UpdatedAtUtc, loaded2.UpdatedAtUtc);
        Assert.Equal(task2.UserId, loaded2.UserId);
    }

    [Fact]
    public async Task SeedSampleData_WhenFileMissing_SeedStays_WhenFileExists_SeedIsOverwritten()
    {
        var fakeStorage = new FakeFileStorage();
        var persistenceOptions = new JsonPersistenceOptions { Enabled = true, FilePath = "test/seed_test.json" };

        var repoMissing = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = true },
            TimeProvider.System,
            fakeStorage,
            persistenceOptions);

        Assert.Equal(4, await repoMissing.CountAsync());
        await repoMissing.LoadAsync();
        Assert.Equal(4, await repoMissing.CountAsync());

        var persistedId = Guid.NewGuid();
        var fileJson = @"[
            {
                ""Id"": """ + persistedId + @""",
                ""Title"": ""Persisted Task"",
                ""Priority"": ""High"",
                ""IsCompleted"": false,
                ""CreatedAtUtc"": ""2026-01-01T00:00:00Z""
            }
        ]";
        fakeStorage.Files[persistenceOptions.FilePath] = fileJson;

        var repoExisting = new InMemoryTodoRepository(
            new InMemoryTodoOptions { SeedSampleData = true },
            TimeProvider.System,
            fakeStorage,
            persistenceOptions);

        Assert.Equal(4, await repoExisting.CountAsync());
        await repoExisting.LoadAsync();

        var loaded = await repoExisting.GetAllAsync();
        Assert.Single(loaded);
        Assert.Equal(persistedId, loaded[0].Id);
        Assert.Equal("Persisted Task", loaded[0].Title);
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
    public async Task Shutdown_ApplicationStopping_ShouldTriggerSaveWithAllCurrentTasks()
    {
        var fakeStorage = new FakeFileStorage();
        var persistenceOptions = new JsonPersistenceOptions { Enabled = true, FilePath = "test/shutdown_stopping.json" };
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

        var task1 = new TodoItem { Id = Guid.NewGuid(), Title = "Task Alpha", Priority = TodoPriority.Normal, CreatedAtUtc = DateTimeOffset.UtcNow };
        var task2 = new TodoItem { Id = Guid.NewGuid(), Title = "Task Beta", Priority = TodoPriority.High, CreatedAtUtc = DateTimeOffset.UtcNow };
        await repository.AddAsync(task1);
        await repository.AddAsync(task2);

        lifetime.StopApplication();

        Assert.Equal(1, fakeStorage.WriteCount);
        Assert.Equal(persistenceOptions.FilePath, fakeStorage.LastWrittenPath);

        var savedTasks = JsonSerializer.Deserialize<List<TodoItem>>(fakeStorage.LastWrittenContent!, JsonOptions);
        Assert.NotNull(savedTasks);
        Assert.Equal(2, savedTasks.Count);
        Assert.Contains(savedTasks, t => t.Id == task1.Id && t.Title == "Task Alpha");
        Assert.Contains(savedTasks, t => t.Id == task2.Id && t.Title == "Task Beta");
    }

    [Fact]
    public async Task Shutdown_StopAsync_ShouldTriggerSaveWithAllCurrentTasks()
    {
        var fakeStorage = new FakeFileStorage();
        var persistenceOptions = new JsonPersistenceOptions { Enabled = true, FilePath = "test/shutdown_stopasync.json" };
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

        var task1 = new TodoItem { Id = Guid.NewGuid(), Title = "Task One", Priority = TodoPriority.Low, CreatedAtUtc = DateTimeOffset.UtcNow };
        var task2 = new TodoItem { Id = Guid.NewGuid(), Title = "Task Two", Priority = TodoPriority.High, CreatedAtUtc = DateTimeOffset.UtcNow };
        await repository.AddAsync(task1);
        await repository.AddAsync(task2);

        await hostedService.StopAsync(CancellationToken.None);

        Assert.True(fakeStorage.WriteCount >= 1);
        Assert.Equal(persistenceOptions.FilePath, fakeStorage.LastWrittenPath);

        var savedTasks = JsonSerializer.Deserialize<List<TodoItem>>(fakeStorage.LastWrittenContent!, JsonOptions);
        Assert.NotNull(savedTasks);
        Assert.Equal(2, savedTasks.Count);
        Assert.Contains(savedTasks, t => t.Id == task1.Id && t.Title == "Task One");
        Assert.Contains(savedTasks, t => t.Id == task2.Id && t.Title == "Task Two");
    }
}
'@

# ---------------------------------------------------------------------
# 4. TodoPersistenceIntegrationTests.cs (Ovìøení LogLevel.Error)
# ---------------------------------------------------------------------

Write-Utf8File "tests/UAI.ZeroVP.Tests/TodoPersistenceIntegrationTests.cs" @'
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
'@

# ---------------------------------------------------------------------
# 5. README.md (Zápis pøes pole øetìzcù – bezpeèné vùèi parseru)
# ---------------------------------------------------------------------

$readmePath = "README.md"
if (Test-Path $readmePath) {
    $readmeContent = [System.IO.File]::ReadAllText($readmePath, [System.Text.Encoding]::UTF8)

    $oldStorageSectionRegex = "(?s)## Úložištì dat.*?(?=## Spuštìní)"
    $newStorageLines = @(
        '## Úložištì dat',
        '',
        'Data žijí v pamìovém úložišti procesu (`ConcurrentDictionary`), které je pøi bìžném chodu aplikace doplnìno o perzistenci do souboru JSON (`JsonPersistence`). Pøi startu aplikace se úkoly naètou ze souboru a pøi ukonèení se do nìj automaticky uloží (`TodoPersistenceHostedService`).',
        '',
        '### Konfigurace perzistence (JsonPersistence)',
        '',
        'Ukládání a naèítání z disku se konfiguruje v sekci `JsonPersistence`:',
        '',
        '```jsonc',
        '// appsettings.json',
        '"JsonPersistence": {',
        '  "Enabled": true,',
        '  "FilePath": "data/todos.json"',
        '}',
        '```',
        '',
        '- `Enabled` (`bool`, výchozí `true`) – povoluje nebo zakazuje diskovou perzistenci.',
        '- `FilePath` (`string`, výchozí `data/todos.json`) – relativní nebo absolutní cesta k souboru s úkoly.',
        '',
        'Nastavení lze pøepsat promìnnými prostøedí (oddìlovaè sekcí `__`):',
        '```bash',
        'JsonPersistence__Enabled=true',
        'JsonPersistence__FilePath=data/todos.json',
        '```',
        '',
        '### Chování pøi startu a ukázková data (Seeding)',
        '',
        'Pøi startu aplikace bez existujícího souboru se vkládají ètyøi ukázkové úkoly (pokud je `Todo:SeedSampleData` zapnuto). Pokud soubor s perzistovanými úkoly existuje, data ze souboru mají pøednost a ukázková data se pøepíší obsahem souboru.',
        '',
        'Ukázková data lze vypnout nastavením:',
        '',
        '```jsonc',
        '// appsettings.json',
        '"Todo": { "SeedSampleData": false }',
        '```',
        '',
        'nebo promìnnou prostøedí `Todo__SeedSampleData=false`.',
        '',
        ''
    ) -join "`r`n"

    if ($readmeContent -match $oldStorageSectionRegex) {
        $readmeContent = [System.Text.RegularExpressions.Regex]::Replace($readmeContent, $oldStorageSectionRegex, $newStorageLines)
        [System.IO.File]::WriteAllText($readmePath, $readmeContent, [System.Text.Encoding]::UTF8)
        Write-Host "  [+] README.md aktualizovano" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------
# 6. Spuštìní testù
# ---------------------------------------------------------------------

Write-Host "`n>>> Vše aktualizováno. Spouštím dotnet test..." -ForegroundColor Cyan
dotnet test --filter "Category!=Performance"