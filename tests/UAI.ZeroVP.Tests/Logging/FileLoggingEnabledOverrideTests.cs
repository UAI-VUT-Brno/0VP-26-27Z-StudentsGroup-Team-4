using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace UAI.ZeroVP.Tests.Logging;

/// <summary>
/// 0VP-T4-F14: <c>FileLogging:Enabled</c> must be read from the final configuration, so an
/// override added with <c>ConfigureAppConfiguration</c> (tests, hosting) takes effect.
/// </summary>
public sealed class FileLoggingEnabledOverrideTests
{
    private static async Task<int> CountLogFilesAfterOneRequestAsync(string logDirectory, string enabled)
    {
        using (var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FileLogging:Enabled"] = enabled,
                    ["FileLogging:Directory"] = logDirectory
                }))))
        {
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/api/info");
            response.EnsureSuccessStatusCode();
        }

        // Disposing the factory disposes the file logger provider, which flushes its queue.
        return Directory.Exists(logDirectory) ? Directory.GetFiles(logDirectory, "*.log").Length : 0;
    }

    [Fact]
    public async Task OverrideToFalse_WritesNoLogFile()
    {
        using var logs = new TempDir();

        Assert.Equal(0, await CountLogFilesAfterOneRequestAsync(logs.FullPath, "false"));
    }

    [Fact]
    public async Task OverrideToTrue_WritesLogFile()
    {
        using var logs = new TempDir();

        Assert.Equal(1, await CountLogFilesAfterOneRequestAsync(logs.FullPath, "true"));
    }
}
