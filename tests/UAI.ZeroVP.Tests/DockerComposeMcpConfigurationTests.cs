using System.Collections.Concurrent;
using System.Net;
using System.Text;
using UAI.ZeroVP.Tests.Logging;

namespace UAI.ZeroVP.Tests;

/// <summary>
/// Guards against a regression of 0VP-T4-F10: <c>docker-compose.yml</c> runs the <c>api</c> service
/// with <c>ASPNETCORE_ENVIRONMENT=Development</c> (for Swagger and sample-data seeding), which makes
/// <c>appsettings.Development.json</c> - and its <c>Mcp:Enabled=true</c> - apply. Without an explicit
/// <c>Mcp__Enabled=false</c> override in <c>docker-compose.yml</c> itself, every replica behind the
/// gateway would serve the unauthenticated <c>/mcp</c> endpoint on the published host port.
/// <para>
/// Two independent checks: a static read of <c>docker-compose.yml</c> (catches the override being
/// removed or typo'd, without needing Docker) and a real process started with the same effective
/// environment variables the <c>api</c> service runs with (catches a regression in how the
/// application itself resolves the setting).
/// </para>
/// </summary>
[Trait("Category", "Mcp")]
public sealed class DockerComposeStaticConfigurationTests
{
    /// <summary>Finds the repository root by walking up from the test binaries to <c>UAI.ZeroVP.sln</c>.</summary>
    internal static string FindRepositoryRoot()
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

        return directory.FullName;
    }

    /// <summary>
    /// Extracts the <c>environment:</c> list of one top-level Compose service (for example <c>api:</c>)
    /// using indentation alone - good enough for this project's own, hand-written <c>docker-compose.yml</c>,
    /// without taking a YAML-parsing dependency just for a test.
    /// </summary>
    internal static IReadOnlyList<string> ReadServiceEnvironment(IReadOnlyList<string> lines, string serviceName)
    {
        var serviceHeader = $"  {serviceName}:";
        var serviceStart = Array.FindIndex(lines.ToArray(), line => line.TrimEnd() == serviceHeader);

        if (serviceStart < 0)
        {
            throw new InvalidOperationException($"Service '{serviceName}:' was not found in docker-compose.yml.");
        }

        var serviceEnd = lines.Count;

        for (var i = serviceStart + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // The next non-blank line indented by exactly two spaces starts the next service (or a
            // top-level key such as "volumes:"), whether it is a real key or just a comment line -
            // either way the api service's own block, including its "environment:" list, is behind us.
            if (trimmed.Length > 0 && line.Length - trimmed.Length == 2)
            {
                serviceEnd = i;
                break;
            }
        }

        var environmentHeader = lines
            .Skip(serviceStart + 1)
            .Take(serviceEnd - serviceStart - 1)
            .ToList();

        var environmentStart = environmentHeader.FindIndex(line => line.TrimEnd() == "    environment:");

        if (environmentStart < 0)
        {
            throw new InvalidOperationException($"Service '{serviceName}:' has no 'environment:' block in docker-compose.yml.");
        }

        var values = new List<string>();

        for (var i = environmentStart + 1; i < environmentHeader.Count; i++)
        {
            var line = environmentHeader[i];
            var trimmed = line.TrimStart();

            if (line.Length == 0 || trimmed.Length == 0)
            {
                continue;
            }

            // A line indented less than the "      - " list entries ends the environment block.
            var indent = line.Length - trimmed.Length;

            if (indent < 6)
            {
                break;
            }

            if (trimmed.StartsWith('#'))
            {
                continue; // comment line between entries
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                values.Add(trimmed[2..].Trim());
            }
        }

        return values;
    }

    [Fact]
    public void ComposeFile_SetsMcpEnabledFalse_ForTheApiService()
    {
        var root = FindRepositoryRoot();
        var composePath = Path.Combine(root, "docker-compose.yml");
        Assert.True(File.Exists(composePath), $"docker-compose.yml was not found at '{composePath}'.");

        var lines = File.ReadAllLines(composePath);
        var apiEnvironment = ReadServiceEnvironment(lines, "api");

        Assert.Contains("ASPNETCORE_ENVIRONMENT=Development", apiEnvironment);
        Assert.Contains(
            "Mcp__Enabled=false",
            apiEnvironment);
    }
}

/// <summary>
/// Starts the real application with the same effective environment the <c>api</c> service in
/// <c>docker-compose.yml</c> runs with (<c>ASPNETCORE_ENVIRONMENT=Development</c> plus its explicit
/// overrides) and checks that <c>/mcp</c> still answers <c>404</c>, while the REST API keeps working.
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

    [Fact(Timeout = 60_000)]
    public async Task McpEndpoint_IsNotServed_WithTheDockerComposeApiEnvironment()
    {
        var server = await ApiProcess.StartAsync(
            "compose-api",
            Path.Combine(_temp.FullPath, "shared", "todos.json"),
            Path.Combine(_temp.FullPath, "logs"),
            _processes,
            new Dictionary<string, string>
            {
                // Mirrors docker-compose.yml's "api" service: Development on, Swagger and sample
                // data on, but Mcp__Enabled explicitly off.
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["Swagger__Enabled"] = "true",
                ["Todo__SeedSampleData"] = "true",
                ["Mcp__Enabled"] = "false"
            });
        using var client = server.CreateClient();

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var mcp = await client.PostAsync("/mcp", content);
        using var rest = await client.GetAsync("/api/todos");

        Assert.Equal(HttpStatusCode.NotFound, mcp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rest.StatusCode);
    }
}
