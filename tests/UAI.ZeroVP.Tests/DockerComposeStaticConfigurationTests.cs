namespace UAI.ZeroVP.Tests;

/// <summary>
/// Guards against a regression of 0VP-T4-F10: <c>docker-compose.yml</c> runs the <c>api</c> service
/// with <c>ASPNETCORE_ENVIRONMENT=Development</c> (for Swagger and sample-data seeding), which makes
/// <c>appsettings.Development.json</c> - and its <c>Mcp:Enabled=true</c> - apply. Without an explicit
/// <c>Mcp__Enabled=false</c> override in <c>docker-compose.yml</c> itself, every replica behind the
/// gateway would serve the unauthenticated <c>/mcp</c> endpoint on the published host port.
/// <para>
/// This class is the static check: it reads <c>docker-compose.yml</c> and fails when the effective
/// <c>Mcp__Enabled</c> value of the <c>api</c> service is not <c>false</c>, without needing Docker.
/// <see cref="DockerComposeMcpBehaviorTests"/> then starts the real application with the environment
/// taken from the same file.
/// </para>
/// <para>
/// Limits: the parser understands only this project's hand-written list-style <c>environment:</c>
/// block. It does not follow <c>env_file:</c>, <c>docker-compose.override.yml</c> or <c>${...}</c>
/// substitution.
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
    internal static IReadOnlyList<string> ReadServiceEnvironment(string[] lines, string serviceName)
    {
        var serviceHeader = $"  {serviceName}:";
        var serviceStart = Array.FindIndex(lines, line => line.TrimEnd() == serviceHeader);

        if (serviceStart < 0)
        {
            throw new InvalidOperationException($"Service '{serviceName}:' was not found in docker-compose.yml.");
        }

        var serviceEnd = lines.Length;

        for (var i = serviceStart + 1; i < lines.Length; i++)
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

        var serviceBlock = lines
            .Skip(serviceStart + 1)
            .Take(serviceEnd - serviceStart - 1)
            .ToList();

        var environmentStart = serviceBlock.FindIndex(line => line.TrimEnd() == "    environment:");

        if (environmentStart < 0)
        {
            throw new InvalidOperationException($"Service '{serviceName}:' has no 'environment:' block in docker-compose.yml.");
        }

        var values = new List<string>();

        for (var i = environmentStart + 1; i < serviceBlock.Count; i++)
        {
            var line = serviceBlock[i];
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

    /// <summary>
    /// Turns <c>NAME=value</c> list entries into a dictionary the way Compose resolves them: surrounding
    /// quotes are removed and, when a name appears more than once, the last entry wins.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ParseEnvironment(IEnumerable<string> entries)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var unquoted = Unquote(entry);
            var separator = unquoted.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                continue; // "NAME" without a value is inherited from the host; not used by this project
            }

            result[unquoted[..separator].Trim()] = Unquote(unquoted[(separator + 1)..].Trim());
        }

        return result;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]
            ? value[1..^1]
            : value;

    /// <summary>Reads <c>docker-compose.yml</c> from the repository root and returns the <c>api</c> service's environment.</summary>
    internal static IReadOnlyDictionary<string, string> ReadApiEnvironment()
    {
        var composePath = Path.Combine(FindRepositoryRoot(), "docker-compose.yml");
        Assert.True(File.Exists(composePath), $"docker-compose.yml was not found at '{composePath}'.");

        return ParseEnvironment(ReadServiceEnvironment(File.ReadAllLines(composePath), "api"));
    }

    [Fact]
    public void ComposeFile_SetsMcpEnabledFalse_ForTheApiService()
    {
        var apiEnvironment = ReadApiEnvironment();

        Assert.True(
            apiEnvironment.TryGetValue("Mcp__Enabled", out var mcpEnabled),
            "docker-compose.yml must set Mcp__Enabled explicitly on the 'api' service.");
        Assert.Equal("false", mcpEnabled, ignoreCase: true);
    }

    [Fact]
    public void ParseEnvironment_LastDuplicateWins_AndQuotesAreRemoved()
    {
        var parsed = ParseEnvironment(new[] { "Mcp__Enabled=false", "\"Mcp__Enabled=true\"", "Other='x'" });

        Assert.Equal("true", parsed["Mcp__Enabled"]);
        Assert.Equal("x", parsed["Other"]);
    }
}
