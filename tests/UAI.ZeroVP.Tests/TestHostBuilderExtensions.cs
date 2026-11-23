using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace UAI.ZeroVP.Tests;

/// <summary>Shared configuration of the in-memory test host built with <c>WebApplicationFactory</c>.</summary>
public static class TestHostBuilderExtensions
{
    /// <summary>
    /// Turns JSON persistence off (<c>JsonPersistence:Enabled=false</c>), so the API under test keeps its data
    /// in memory only and never reads or writes <c>data/todos.json</c> in the repository. Production code
    /// does not detect tests; every test host that is not about persistence calls this explicitly.
    /// A test that needs a file (shared storage) adds its own <c>JsonPersistence:*</c> settings afterwards,
    /// because configuration sources added later override earlier ones.
    /// </summary>
    public static IWebHostBuilder DisableJsonPersistence(this IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JsonPersistence:Enabled"] = "false"
            }));
    }
}
