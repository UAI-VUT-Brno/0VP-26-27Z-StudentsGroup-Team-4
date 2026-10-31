using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using UAI.ZeroVP.Tests.Logging; // Obsahuje LogTestHelpers s metodou RedirectLogsToTemp

namespace UAI.ZeroVP.Tests;

/// <summary>
/// Two (or more) complete API hosts configured with the same shared JSON file.
/// What is created through one instance must be visible through the other.
/// </summary>
public sealed class SharedStorageApiTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ConcurrentBag<WebApplicationFactory<Program>> _factories = new();

    private string DataFile => Path.Combine(_temp.FullPath, "shared", "todos.json");

    public void Dispose()
    {
        DisposeInstances();
        _temp.Dispose();
    }

    private void DisposeInstances()
    {
        while (_factories.TryTake(out var factory))
        {
            factory.Dispose();
        }
    }

    private HttpClient StartInstance(string id, bool seed = false, bool shared = true)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Instance:Id"] = id,
            ["Todo:SeedSampleData"] = seed ? "true" : "false",
            ["FileLogging:Directory"] = Path.Combine(_temp.FullPath, "logs")
        };

        if (shared)
        {
            settings["JsonPersistence:Enabled"] = "true";
            settings["JsonPersistence:Shared"] = "true";
            settings["JsonPersistence:FilePath"] = DataFile;
        }

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Přesměruje objemné logovací soubory z testovací instance do dočasné složky 
            // nezávisle na appsettings.json.
            builder.RedirectLogsToTemp();
            
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        });

        _factories.Add(factory);

        return factory.CreateClient();
    }

    private static async Task<Guid> CreateTaskAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/todos", new { title });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ListIdsAsync(HttpClient client)
    {
        var body = await client.GetFromJsonAsync<JsonElement>("/api/todos");

        return body.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
    }

    private int TasksInFile() => JsonDocument.Parse(File.ReadAllText(DataFile)).RootElement.GetArrayLength();

    [Fact]
    public async Task TaskCreatedOnInstanceA_IsVisibleOnInstanceB()
    {
        var a = StartInstance("instance-a");
        var b = StartInstance("instance-b");

        var id = await CreateTaskAsync(a, "created on A");

        using var response = await b.GetAsync($"/api/todos/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("created on A", task.GetProperty("title").GetString());
        Assert.Contains(id, await ListIdsAsync(b));
    }

    [Fact]
    public async Task UpdateAndDeleteOnOneInstance_AreVisibleOnTheOther()
    {
        var a = StartInstance("instance-a");
        var b = StartInstance("instance-b");
        var id = await CreateTaskAsync(a, "to be completed and deleted");

        using (var completed = await b.PostAsync($"/api/todos/{id}/complete", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        }

        var seenOnA = await a.GetFromJsonAsync<JsonElement>($"/api/todos/{id}");
        Assert.True(seenOnA.GetProperty("isCompleted").GetBoolean());

        using (var deleted = await a.DeleteAsync($"/api/todos/{id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using var gone = await b.GetAsync($"/api/todos/{id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task SimultaneousWritesToBothInstances_LoseNothing_AndLeaveValidJson()
    {
        var a = StartInstance("instance-a");
        var b = StartInstance("instance-b");

        var ids = await Task.WhenAll(Enumerable.Range(0, 80)
            .Select(i => Task.Run(() => CreateTaskAsync(i % 2 == 0 ? a : b, $"task-{i}"))));

        Assert.Equal(80, ids.Distinct().Count());
        Assert.Equal(80, (await ListIdsAsync(a)).Count);
        Assert.Equal(80, (await ListIdsAsync(b)).Count);
        Assert.Equal(80, TasksInFile());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(DataFile)!, "*.tmp"));
    }

    [Fact]
    public async Task InstancesStartingAtTheSameTime_SeedTheSharedFileOnlyOnce()
    {
        var clients = await Task.WhenAll(
            Task.Run(() => StartInstance("instance-a", seed: true)),
            Task.Run(() => StartInstance("instance-b", seed: true)),
            Task.Run(() => StartInstance("instance-c", seed: true)));

        foreach (var client in clients)
        {
            Assert.Equal(4, (await ListIdsAsync(client)).Count);
        }

        Assert.Equal(4, TasksInFile());
    }

    [Fact]
    public async Task Data_SurvivesRestartOfAllInstances()
    {
        var first = StartInstance("instance-a");
        var id = await CreateTaskAsync(first, "survives restart");

        DisposeInstances();

        var restarted = StartInstance("instance-b");
        Assert.Contains(id, await ListIdsAsync(restarted));
    }

    [Fact]
    public async Task Info_ReportsTheSharedStorage_AndBothInstancesPointToTheSameFile()
    {
        var a = StartInstance("instance-a");
        var b = StartInstance("instance-b");

        var infoA = await a.GetFromJsonAsync<JsonElement>("/api/info");
        var infoB = await b.GetFromJsonAsync<JsonElement>("/api/info");

        Assert.Equal("instance-a", infoA.GetProperty("instanceId").GetString());
        Assert.Equal("instance-b", infoB.GetProperty("instanceId").GetString());

        foreach (var storage in new[] { infoA.GetProperty("storage"), infoB.GetProperty("storage") })
        {
            Assert.Equal("SharedJsonFile", storage.GetProperty("type").GetString());
            Assert.True(storage.GetProperty("persistent").GetBoolean());
            Assert.True(storage.GetProperty("shared").GetBoolean());
            Assert.Equal(Path.GetFullPath(DataFile), storage.GetProperty("location").GetString());
        }
    }

    [Fact]
    public async Task Info_WithoutSharedStorage_ReportsInMemoryStorage()
    {
        var client = StartInstance("instance-a", shared: false);

        var storage = (await client.GetFromJsonAsync<JsonElement>("/api/info")).GetProperty("storage");

        Assert.Equal("InMemory", storage.GetProperty("type").GetString());
        Assert.False(storage.GetProperty("shared").GetBoolean());
        Assert.Equal(JsonValueKind.Null, storage.GetProperty("location").ValueKind);
    }
}