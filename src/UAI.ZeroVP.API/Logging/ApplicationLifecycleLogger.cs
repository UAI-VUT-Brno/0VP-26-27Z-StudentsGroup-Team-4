using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace UAI.ZeroVP.API.Logging;

/// <summary>
/// Zapisuje do logu start a zastavení aplikace (události 1000–1004).
/// <para>
/// <c>ApplicationStopping</c> a <c>ApplicationStopped</c> přijdou při Ctrl+C,
/// signálu SIGTERM i <c>docker stop</c>. Při násilném ukončení (SIGKILL, pád procesu,
/// <c>docker kill</c>) se nezapíše nic – aplikace o něm nemá šanci vědět.
/// </para>
/// </summary>
public sealed class ApplicationLifecycleLogger
{
    private readonly ILogger<ApplicationLifecycleLogger> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IHostEnvironment _environment;
    private readonly IServer _server;

    /// <summary>Vytvoří službu se závislostmi z DI kontejneru.</summary>
    public ApplicationLifecycleLogger(
        ILogger<ApplicationLifecycleLogger> logger,
        IHostApplicationLifetime lifetime,
        IHostEnvironment environment,
        IServer server)
    {
        _logger = logger;
        _lifetime = lifetime;
        _environment = environment;
        _server = server;
    }

    /// <summary>
    /// Zapíše událost <c>ApplicationStarting</c> a přihlásí se k odběru
    /// ostatních událostí životního cyklu. Volá se jednou, těsně před startem serveru.
    /// </summary>
    public void LogStarting()
    {
        _lifetime.ApplicationStarted.Register(OnStarted);
        _lifetime.ApplicationStopping.Register(OnStopping);
        _lifetime.ApplicationStopped.Register(OnStopped);

        var version = typeof(ApplicationLifecycleLogger).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "neznámá";

        _logger.LogInformation(LogEvents.ApplicationStarting,
            "Aplikace se spouští (prostředí {Environment}, verze {Version}).",
            _environment.EnvironmentName, version);
    }

    /// <summary>Zapíše událost <c>ApplicationFailed</c> – start nebo běh skončil chybou.</summary>
    public void LogFailed(Exception exception) =>
        _logger.LogCritical(LogEvents.ApplicationFailed, exception, "Aplikace skončila neočekávanou chybou.");

    private void OnStarted()
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var urls = addresses is null ? string.Empty : string.Join(", ", addresses);

        _logger.LogInformation(LogEvents.ApplicationStarted,
            "Aplikace byla spuštěna a naslouchá na {Urls}.", urls);
    }

    private void OnStopping() =>
        _logger.LogInformation(LogEvents.ApplicationStopping, "Aplikace se zastavuje.");

    private void OnStopped() =>
        _logger.LogInformation(LogEvents.ApplicationStopped, "Aplikace byla zastavena.");
}
