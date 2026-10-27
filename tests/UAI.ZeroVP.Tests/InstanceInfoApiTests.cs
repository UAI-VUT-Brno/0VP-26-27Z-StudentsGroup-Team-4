using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using UAI.ZeroVP.Tests.Logging;

namespace UAI.ZeroVP.Tests;

/// <summary>
/// Several instances of the real application side by side (in-memory test servers):
/// every instance reports its own id and port in GET /api/info and writes its own log file.
/// </summary>
public sealed class InstanceInfoApiTests
{
    private static WebApplicationFactory<Program> CreateInstance(
        string logDirectory,
        string? instanceId,
        string? urls,
        bool includeInstanceIdInLogName)
    {
        var settings = new Dictionary<string, string?>
        {
            ["FileLogging:Directory"] = logDirectory,
            ["FileLogging:IncludeInstanceId"] = includeInstanceIdInLogName ? "true" : "false"
        };

        if (instanceId is not null)
        {
            settings["Instance:Id"] = instanceId;
        }

        if (urls is not null)
        {
            settings["urls"] = urls;
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings)));
    }

    private static async Task<JsonElement> GetInfoAsync(WebApplicationFactory<Program> instance)
    {
        using var client = instance.CreateClient();
        using var response = await client.GetAsync("/api/info");

        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Info_ReturnsConfiguredInstanceIdAndPort()
    {
        using var temp = new TempDir();
        await using var instance = CreateInstance(temp.FullPath, "alpha", "http://localhost:5301", includeInstanceIdInLogName: true);

        var info = await GetInfoAsync(instance);

        Assert.Equal("alpha", info.GetProperty("instanceId").GetString());
        Assert.Equal(5301, info.GetProperty("port").GetInt32());
        Assert.Contains(
            info.GetProperty("urls").EnumerateArray().Select(url => url.GetString()),
            url => url!.Contains(":5301", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Info_WithoutConfiguredId_DerivesIdFromMachineNameAndPort()
    {
        using var temp = new TempDir();
        await using var instance = CreateInstance(temp.FullPath, null, "http://localhost:5303", includeInstanceIdInLogName: false);

        var info = await GetInfoAsync(instance);

        Assert.Equal($"{Environment.MachineName}-5303", info.GetProperty("instanceId").GetString());
    }

    [Fact]
    public async Task Info_KeepsAllExistingFields()
    {
        using var temp = new TempDir();
        await using var instance = CreateInstance(temp.FullPath, "alpha", "http://localhost:5301", includeInstanceIdInLogName: false);

        var info = await GetInfoAsync(instance);

        foreach (var field in new[] { "machineName", "environment", "version", "utcNow", "uptime", "todoCount" })
        {
            Assert.True(info.TryGetProperty(field, out _), $"Field '{field}' is missing from /api/info.");
        }

        Assert.Equal(Environment.MachineName, info.GetProperty("machineName").GetString());
    }

    [Fact]
    public async Task TwoInstances_ReportDifferentIdAndPort()
    {
        using var temp = new TempDir();
        await using var first = CreateInstance(temp.FullPath, "alpha", "http://localhost:5301", includeInstanceIdInLogName: true);
        await using var second = CreateInstance(temp.FullPath, "beta", "http://localhost:5302", includeInstanceIdInLogName: true);

        var firstInfo = await GetInfoAsync(first);
        var secondInfo = await GetInfoAsync(second);

        Assert.NotEqual(firstInfo.GetProperty("instanceId").GetString(), secondInfo.GetProperty("instanceId").GetString());
        Assert.Equal(5301, firstInfo.GetProperty("port").GetInt32());
        Assert.Equal(5302, secondInfo.GetProperty("port").GetInt32());
    }

    [Fact]
    public async Task TwoInstances_WriteToTheirOwnLogFiles()
    {
        using var temp = new TempDir();

        var first = CreateInstance(temp.FullPath, "alpha", "http://localhost:5301", includeInstanceIdInLogName: true);
        var second = CreateInstance(temp.FullPath, "beta", "http://localhost:5302", includeInstanceIdInLogName: true);

        await GetInfoAsync(first);
        await GetInfoAsync(second);

        // Disposing the host closes the log provider, which flushes the queue to disk.
        await first.DisposeAsync();
        await second.DisposeAsync();

        var alphaFile = Assert.Single(Directory.GetFiles(temp.FullPath, "zerovp-alpha-*.log"));
        var betaFile = Assert.Single(Directory.GetFiles(temp.FullPath, "zerovp-beta-*.log"));
        Assert.NotEqual(alphaFile, betaFile);

        Assert.Contains("/api/info", string.Join('\n', LogFiles.ReadLines(alphaFile)));
        Assert.Contains("/api/info", string.Join('\n', LogFiles.ReadLines(betaFile)));
    }

    [Fact]
    public async Task DefaultConfiguration_KeepsOriginalLogFileName()
    {
        // IncludeInstanceId is off by default: a single instance writes zerovp-yyyyMMdd.log as before,
        // even though it has an instance id.
        using var temp = new TempDir();
        var instance = CreateInstance(temp.FullPath, "alpha", "http://localhost:5301", includeInstanceIdInLogName: false);

        await GetInfoAsync(instance);
        await instance.DisposeAsync();

        var name = Path.GetFileName(Assert.Single(Directory.GetFiles(temp.FullPath)));
        Assert.Matches(new Regex(@"^zerovp-\d{8}\.log$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), name);
    }
}