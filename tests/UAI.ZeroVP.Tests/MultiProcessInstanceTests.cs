using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UAI.ZeroVP.Tests.Logging;

namespace UAI.ZeroVP.Tests;

/// <summary>
/// The real application started as separate operating-system processes with real Kestrel servers,
/// exactly like <c>scripts/run-instances</c> does it. This is the only place where the cross-process
/// file lock, concurrent startup on OS-assigned ports and the shared file are exercised for real.
/// Needs the API built (the test project references it, so a normal build is enough).
/// </summary>
[Trait("Category", "MultiProcess")]
public sealed class MultiProcessInstanceTests : IAsyncLifetime
{
    private readonly TempDir _temp = new();
    private readonly ConcurrentBag<ApiProcess> _processes = new();

    private string DataFile => Path.Combine(_temp.FullPath, "shared", "todos.json");

    private string LogDirectory => Path.Combine(_temp.FullPath, "logs");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var process in _processes)
        {
            await process.DisposeAsync();
        }

        _temp.Dispose();
    }

    private Task<ApiProcess[]> StartInstancesAsync(int count) =>
        Task.WhenAll(Enumerable.Range(1, count)
            .Select(n => ApiProcess.StartAsync($"proc-{n}", DataFile, LogDirectory, _processes)));

    private int TasksInFile() => JsonDocument.Parse(File.ReadAllText(DataFile)).RootElement.GetArrayLength();

    [Fact]
    public async Task ConcurrentStartup_OnPortZero_EveryInstanceBindsItsOwnPort()
    {
        var instances = await StartInstancesAsync(4);

        var ports = instances.Select(instance => new Uri(instance.BaseUrl).Port).ToList();
        Assert.All(ports, port => Assert.True(port > 0));
        Assert.Equal(instances.Length, ports.Distinct().Count());

        foreach (var instance in instances)
        {
            using var client = instance.CreateClient();
            var info = await client.GetFromJsonAsync<JsonElement>("/api/info");

            Assert.Equal(instance.InstanceId, info.GetProperty("instanceId").GetString());
            Assert.Equal(new Uri(instance.BaseUrl).Port, info.GetProperty("port").GetInt32());
            Assert.False(instance.HasExited, instance.Output);
            Assert.DoesNotContain("address already in use", instance.Output, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(instances.Length, instances.Select(instance => instance.InstanceId).Distinct().Count());
    }

    [Fact]
    public async Task TaskCreatedOnInstanceA_IsReadFromInstanceB()
    {
        var instances = await StartInstancesAsync(2);
        using var a = instances[0].CreateClient();
        using var b = instances[1].CreateClient();

        using var created = await a.PostAsJsonAsync("/api/todos", new { title = "created on A" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var seenOnB = await b.GetFromJsonAsync<JsonElement>($"/api/todos/{id}");

        Assert.Equal("created on A", seenOnB.GetProperty("title").GetString());

        var storageA = (await a.GetFromJsonAsync<JsonElement>("/api/info")).GetProperty("storage");
        var storageB = (await b.GetFromJsonAsync<JsonElement>("/api/info")).GetProperty("storage");
        Assert.Equal("SharedJsonFile", storageA.GetProperty("type").GetString());
        Assert.Equal(storageA.GetProperty("location").GetString(), storageB.GetProperty("location").GetString());
    }

    [Fact]
    public async Task SimultaneousWritesToSeveralProcesses_AreNeitherLostNorCorrupted()
    {
        const int instanceCount = 3;
        const int requests = 60;

        var instances = await StartInstancesAsync(instanceCount);
        var clients = instances.Select(instance => instance.CreateClient()).ToArray();

        try
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, requests)
                .Select(i => clients[i % instanceCount].PostAsJsonAsync("/api/todos", new { title = $"task-{i}" })));

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));

            foreach (var client in clients)
            {
                var list = await client.GetFromJsonAsync<JsonElement>("/api/todos");
                Assert.Equal(requests, list.GetArrayLength());
            }

            Assert.Equal(requests, TasksInFile());
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(DataFile)!, "*.tmp"));

            foreach (var instance in instances)
            {
                Assert.False(instance.HasExited, instance.Output);
                Assert.DoesNotContain("Unhandled exception", instance.Output, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("IOException", instance.Output, StringComparison.Ordinal);
            }
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task Data_SurvivesStoppingAllInstances()
    {
        var first = await StartInstancesAsync(2);
        using var client = first[0].CreateClient();
        using var created = await client.PostAsJsonAsync("/api/todos", new { title = "survives restart" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        foreach (var instance in first)
        {
            await instance.DisposeAsync();
        }

        var restarted = await ApiProcess.StartAsync("proc-restarted", DataFile, LogDirectory, _processes);
        using var restartedClient = restarted.CreateClient();

        var task = await restartedClient.GetFromJsonAsync<JsonElement>($"/api/todos/{id}");
        Assert.Equal("survives restart", task.GetProperty("title").GetString());
    }
}

/// <summary>One API process started on an OS-assigned port (<c>--urls http://127.0.0.1:0</c>).</summary>
internal sealed class ApiProcess : IAsyncDisposable
{
    private static readonly Regex ListeningPattern = new(
        @"Now listening on:\s*(?<url>https?://\S+)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private readonly Process _process;
    private readonly TaskCompletionSource<string> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StringBuilder _output = new();
    private readonly object _outputLock = new();

    private ApiProcess(Process process, string instanceId)
    {
        _process = process;
        InstanceId = instanceId;
    }

    public string InstanceId { get; }

    public string BaseUrl { get; private set; } = string.Empty;

    public bool HasExited => _process.HasExited;

    /// <summary>Everything the process wrote to standard output and error so far.</summary>
    public string Output
    {
        get
        {
            lock (_outputLock)
            {
                return _output.ToString();
            }
        }
    }

    public HttpClient CreateClient() => new() { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<ApiProcess> StartAsync(
        string instanceId,
        string dataFile,
        string logDirectory,
        ConcurrentBag<ApiProcess> owner)
    {
        var (dll, projectDirectory) = Locate();

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet",
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(dll);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add("http://127.0.0.1:0");
        startInfo.ArgumentList.Add("--contentRoot");
        startInfo.ArgumentList.Add(projectDirectory);

        foreach (var name in new[] { "ASPNETCORE_URLS", "ASPNETCORE_HTTP_PORTS", "ASPNETCORE_HTTPS_PORTS", "DOTNET_URLS" })
        {
            startInfo.Environment.Remove(name);
        }

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["Instance__Id"] = instanceId;
        startInfo.Environment["Todo__SeedSampleData"] = "false";
        startInfo.Environment["JsonPersistence__Enabled"] = "true";
        startInfo.Environment["JsonPersistence__Shared"] = "true";
        startInfo.Environment["JsonPersistence__FilePath"] = dataFile;
        startInfo.Environment["FileLogging__Directory"] = logDirectory;
        startInfo.Environment["FileLogging__IncludeInstanceId"] = "true";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var instance = new ApiProcess(process, instanceId);
        owner.Add(instance);

        process.OutputDataReceived += (_, e) => instance.OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => instance.OnLine(e.Data);
        process.Exited += (_, _) => instance._listening.TrySetException(new InvalidOperationException(
            $"Instance '{instanceId}' exited before it started listening.{Environment.NewLine}{instance.Output}"));

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var finished = await Task.WhenAny(instance._listening.Task, Task.Delay(TimeSpan.FromSeconds(60)));

        if (finished != instance._listening.Task)
        {
            throw new TimeoutException($"Instance '{instanceId}' did not start listening within 60 s.{Environment.NewLine}{instance.Output}");
        }

        instance.BaseUrl = await instance._listening.Task;

        return instance;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _process.WaitForExitAsync(timeout.Token);
        }
        catch (InvalidOperationException)
        {
            // The process is already gone.
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnLine(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_outputLock)
        {
            _output.AppendLine(line);
        }

        var match = ListeningPattern.Match(line);

        if (match.Success)
        {
            _listening.TrySetResult(match.Groups["url"].Value);
        }
    }

    /// <summary>Finds the built API (<c>UAI.ZeroVP.API.dll</c> with its runtimeconfig) next to the solution.</summary>
    private static (string Dll, string ProjectDirectory) Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UAI.ZeroVP.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException($"UAI.ZeroVP.sln was not found above '{AppContext.BaseDirectory}'.");
        }

        var projectDirectory = Path.Combine(directory.FullName, "src", "UAI.ZeroVP.API");
        var binDirectory = Path.Combine(projectDirectory, "bin");

        var dll = Directory.Exists(binDirectory)
            ? Directory.GetFiles(binDirectory, "UAI.ZeroVP.API.dll", SearchOption.AllDirectories)
                .Where(path => File.Exists(Path.ChangeExtension(path, ".runtimeconfig.json")))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        return dll is null
            ? throw new InvalidOperationException($"The built API was not found under '{binDirectory}'. Build the solution first.")
            : (dll, projectDirectory);
    }
}