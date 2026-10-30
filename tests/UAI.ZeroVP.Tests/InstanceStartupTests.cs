using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.API.Instances;

namespace UAI.ZeroVP.Tests;

/// <summary>
/// Real Kestrel servers (not the in-memory test server) started side by side on OS-assigned ports.
/// Shows that nothing hardcodes a port and that the identity reports the port really bound.
/// </summary>
public sealed class InstanceStartupTests
{
    private static WebApplication BuildHost(string url)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(url);
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.MapGet("/ping", () => "pong");

        return app;
    }

    private static async Task StopAsync(IEnumerable<WebApplication> hosts)
    {
        foreach (var host in hosts)
        {
            await host.StopAsync();
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task ManyInstancesStartingAtTheSameTime_OnPortZero_GetDifferentPorts()
    {
        var hosts = Enumerable.Range(0, 6).Select(_ => BuildHost("http://127.0.0.1:0")).ToList();

        try
        {
            // No "address already in use": port 0 means the OS hands out a free port to each host.
            await Task.WhenAll(hosts.Select(host => Task.Run(() => host.StartAsync())));

            var identity = new InstanceIdentity("probe", new[] { "http://127.0.0.1:0" });
            var ports = hosts
                .Select(host => InstanceIdentity.TryGetPort(identity.GetListeningUrls(host.Services.GetRequiredService<IServer>()).First()))
                .ToList();

            Assert.All(ports, port => Assert.True(port > 0, "The identity must report the real port, not the configured 0."));
            Assert.Equal(hosts.Count, ports.Distinct().Count());
        }
        finally
        {
            await StopAsync(hosts);
        }
    }

    [Fact]
    public async Task EachInstance_AnswersOnItsOwnReportedAddress()
    {
        var hosts = new[] { BuildHost("http://127.0.0.1:0"), BuildHost("http://127.0.0.1:0") };

        try
        {
            await Task.WhenAll(hosts.Select(host => host.StartAsync()));

            var identity = new InstanceIdentity("probe", new[] { "http://127.0.0.1:0" });
            using var client = new HttpClient();

            foreach (var host in hosts)
            {
                var address = identity.GetListeningUrls(host.Services.GetRequiredService<IServer>()).First();

                Assert.Equal("pong", await client.GetStringAsync($"{address}/ping"));
            }
        }
        finally
        {
            await StopAsync(hosts);
        }
    }
}