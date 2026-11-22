using System.Collections.Concurrent;
using System.Net;
using System.Text;
using UAI.ZeroVP.Tests.Logging;

namespace UAI.ZeroVP.Tests;

/// <summary>
/// Starts the real application with the environment of the <c>api</c> service read from
/// <c>docker-compose.yml</c> (<c>ASPNETCORE_ENVIRONMENT=Development</c> plus its overrides) and checks
/// that <c>/mcp</c> answers <c>404</c> while the REST API keeps working. Container-specific settings
/// (<c>JsonPersistence__*</c>, <c>FileLogging__*</c>) are left out because the test host supplies its
/// own temporary paths. A negative control without <c>Mcp__Enabled</c> proves the test can fail.
/// </summary>
[Trait("Category", "MultiProcess")]
[Trait("Category", "Mcp")]
public sealed class DockerComposeMcpBehaviorTests : IAsyncLifetime
{
    private readonly TempDir _temp = new();
    private readonly ConcurrentBag<ApiProcess> _processes = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var process in _processes)
        {
            await process.DisposeAsync();
        }

        _temp.Dispose();
    }

    /// <summary>The compose <c>api</c> environment minus the settings the test host sets itself.</summary>
    private static Dictionary<string, string> ComposeApiEnvironment() =>
        DockerComposeStaticConfigurationTests.ReadApiEnvironment()
            .Where(pair => !pair.Key.StartsWith("JsonPersistence__", StringComparison.Ordinal)
                && !pair.Key.StartsWith("FileLogging__", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private async Task<(HttpStatusCode Mcp, HttpStatusCode Rest)> RequestAsync(string instanceId, Dictionary<string, string> environment)
    {
        var server = await ApiProcess.StartAsync(
            instanceId,
            Path.Combine(_temp.FullPath, instanceId, "todos.json"),
            Path.Combine(_temp.FullPath, instanceId, "logs"),
            _processes,
            environment);
        using var client = server.CreateClient();

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var mcp = await client.PostAsync("/mcp", content);
        using var rest = await client.GetAsync("/api/todos");

        return (mcp.StatusCode, rest.StatusCode);
    }

    [Fact(Timeout = 60_000)]
    public async Task McpEndpoint_IsNotServed_WithTheDockerComposeApiEnvironment()
    {
        var (mcp, rest) = await RequestAsync("compose-api", ComposeApiEnvironment());

        Assert.Equal(HttpStatusCode.NotFound, mcp);
        Assert.Equal(HttpStatusCode.OK, rest);
    }

    /// <summary>
    /// Negative control: without the explicit override the Development settings turn MCP on. If this
    /// ever returns 404 the process test above is no longer proving anything (for example because
    /// <c>appsettings.Development.json</c> is not loaded in the spawned process).
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task McpEndpoint_IsServed_WhenTheComposeOverrideIsMissing()
    {
        var environment = ComposeApiEnvironment();
        Assert.True(environment.Remove("Mcp__Enabled"), "docker-compose.yml no longer sets Mcp__Enabled for the 'api' service.");

        var (mcp, _) = await RequestAsync("compose-api-no-override", environment);

        Assert.NotEqual(HttpStatusCode.NotFound, mcp);
    }
}
