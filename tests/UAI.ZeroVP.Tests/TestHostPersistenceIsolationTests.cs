using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UAI.ZeroVP.Tests.Logging;

namespace UAI.ZeroVP.Tests;

/// <summary>
/// 0VP-T4-F08: the API does not detect the test runner; the test host switches persistence off itself
/// through <see cref="TestHostBuilderExtensions.DisableJsonPersistence"/>.
/// </summary>
public sealed class TestHostPersistenceIsolationTests : IDisposable
{
    private readonly TempDir _logs = new();
    private readonly WebApplicationFactory<Program> _factory;

    public TestHostPersistenceIsolationTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.RedirectLogsTo(_logs.FullPath).DisableJsonPersistence());
    }

    public void Dispose()
    {
        // Factory first (flushes and closes the log file), then the folder.
        _factory.Dispose();
        _logs.Dispose();
    }

    [Fact]
    public async Task DisableJsonPersistence_ReportsInMemoryStorage()
    {
        using var client = _factory.CreateClient();

        var storage = (await client.GetFromJsonAsync<JsonElement>("/api/info")).GetProperty("storage");

        Assert.Equal("InMemory", storage.GetProperty("type").GetString());
        Assert.False(storage.GetProperty("persistent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, storage.GetProperty("location").ValueKind);
    }
}
