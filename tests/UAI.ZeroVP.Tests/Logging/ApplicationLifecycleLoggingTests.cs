using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.API.Logging;

namespace UAI.ZeroVP.Tests.Logging;

/// <summary>
/// Průchod celým zapojením nad skutečným Kestrelem (náhodný port):
/// start → HTTP požadavky → zastavení. Ověřuje, že poslední záznamy
/// (ApplicationStopped) se po vypnutí hostitele opravdu dostanou do souboru.
/// </summary>
public sealed class ApplicationLifecycleLoggingTests
{
    // Hlášení frameworku (Microsoft.*) jsou v souboru taky, proto se filtruje podle kategorie.
    private const string LifecycleCategory = "UAI.ZeroVP.API.Logging.ApplicationLifecycleLogger";
    private const string RequestCategory = "UAI.ZeroVP.API.Logging.RequestLoggingMiddleware";

    [Fact]
    public async Task FullLifecycle_IsPersisted_StartRequestsStop()
    {
        using var temp = new TempDir();
        var app = BuildApp(temp.FullPath);

        app.Services.GetRequiredService<ApplicationLifecycleLogger>().LogStarting();
        await app.StartAsync();

        var baseAddress = app.Urls.First();
        using (var client = new HttpClient { BaseAddress = new Uri(baseAddress) })
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/ok")).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/missing")).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.InternalServerError, (await client.GetAsync("/boom")).StatusCode);
            await client.GetAsync("/health");
        }

        await app.StopAsync();
        await app.DisposeAsync(); // zavře poskytovatele logu a vyprázdní frontu

        var entries = LogFiles.ReadEntries(temp.FullPath);

        // 1) životní cyklus: přesně čtyři události ve správném pořadí
        var lifecycle = entries.Where(entry => entry.Str("category") == LifecycleCategory).ToList();
        Assert.Equal(
            new[] { "ApplicationStarting", "ApplicationStarted", "ApplicationStopping", "ApplicationStopped" },
            lifecycle.Select(entry => entry.Str("event")));
        Assert.All(lifecycle, entry => Assert.Equal("Success", entry.Str("result")));
        Assert.Contains("127.0.0.1:", lifecycle[1].Props().GetProperty("Urls").GetString());

        // 2) požadavky: úspěch, 404 a výjimka; /health je vynechaný
        var requests = entries.Where(entry => entry.Str("category") == RequestCategory).ToList();
        Assert.Equal(3, requests.Count);

        var ok = requests.Single(entry => entry.Props().GetProperty("Path").GetString() == "/ok");
        Assert.Equal("Success", ok.Str("result"));
        Assert.Equal(200, ok.Props().GetProperty("StatusCode").GetInt32());
        Assert.Equal("ok", ok.Props().GetProperty("Route").GetString()?.TrimStart('/'));

        var missing = requests.Single(entry => entry.Props().GetProperty("Path").GetString() == "/missing");
        Assert.Equal("Failure", missing.Str("result"));
        Assert.Equal("HTTP 404 Not Found", missing.Str("error"));

        var boom = requests.Single(entry => entry.Props().GetProperty("Path").GetString() == "/boom");
        Assert.Equal(2001, boom.Id());
        Assert.Equal("InvalidOperationException: boom", boom.Str("error"));
        Assert.Equal(500, boom.Props().GetProperty("StatusCode").GetInt32());

        Assert.DoesNotContain(requests, entry => entry.Props().GetProperty("Path").GetString() == "/health");
    }

    [Fact]
    public async Task StartupFailure_IsLoggedAsCritical()
    {
        using var temp = new TempDir();
        var app = BuildApp(temp.FullPath);
        var lifecycle = app.Services.GetRequiredService<ApplicationLifecycleLogger>();

        lifecycle.LogFailed(new IOException("Port je obsazený"));
        await app.DisposeAsync();

        var entry = Assert.Single(LogFiles.ReadEntries(temp.FullPath));
        Assert.Equal("Critical", entry.Str("level"));
        Assert.Equal(1004, entry.Id());
        Assert.Equal("ApplicationFailed", entry.Str("event"));
        Assert.Equal("Failure", entry.Str("result"));
        Assert.Equal("IOException: Port je obsazený", entry.Str("error"));
        Assert.Contains("System.IO.IOException", entry.Str("exception"));
    }

    private static WebApplication BuildApp(string logDirectory)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); // konzole v testu nepotřebujeme; souborový provider se přidá až níže
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileLogging:Directory"] = logDirectory,
            ["RequestLogging:ExcludedPaths:0"] = "/health"
        });

        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.AddApplicationLogging();

        var app = builder.Build();

        // Stejné pořadí jako v Program.cs.
        app.UseExceptionHandler();
        app.UseRequestLogging();

        app.MapGet("/ok", () => "ok");
        app.MapGet("/health", () => "healthy");
        app.MapGet("/boom", (Func<string>)(() => throw new InvalidOperationException("boom")));

        return app;
    }
}
