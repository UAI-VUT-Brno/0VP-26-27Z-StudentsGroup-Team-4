using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace UAI.ZeroVP.API.Instances;

/// <summary>
/// Identita běžící instance API: její identifikátor a adresy, na kterých naslouchá.
/// <para>
/// Identifikátor se bere z konfigurace (<c>Instance:Id</c>, v prostředí <c>Instance__Id</c>).
/// Není-li zadán, odvodí se ze jména stroje a portu: <c>{MachineName}-{port}</c>.
/// Port se čte z <c>ASPNETCORE_URLS</c> / <c>--urls</c> / klíče <c>urls</c>, případně z
/// <c>ASPNETCORE_HTTP_PORTS</c> nebo <c>Kestrel:Endpoints</c>. Žádný port není v kódu napevno –
/// bez nastavení platí výchozí port Kestrelu 5000.
/// </para>
/// </summary>
public sealed class InstanceIdentity
{
    /// <summary>Název konfigurační sekce.</summary>
    public const string SectionName = "Instance";

    /// <summary>Adresa, na které Kestrel naslouchá, když není nastavena žádná jiná.</summary>
    public const string DefaultUrl = "http://localhost:5000";

    private static readonly Regex AddressPattern = new(
        @"^\s*(?<scheme>[A-Za-z][A-Za-z0-9+.\-]*)://(?<host>\[[^\]]*\]|[^:/\s]+)(?::(?<port>\d+))?",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>Vytvoří identitu z hotových hodnot (v testech i v <see cref="FromConfiguration"/>).</summary>
    /// <param name="id">Identifikátor instance; nesmí být prázdný.</param>
    /// <param name="configuredUrls">Adresy z konfigurace – použijí se, dokud server nehlásí skutečné.</param>
    public InstanceIdentity(string id, IReadOnlyList<string> configuredUrls)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(configuredUrls);

        Id = id;
        ConfiguredUrls = configuredUrls;
    }

    /// <summary>Identifikátor instance (nastavený, nebo odvozený ze jména stroje a portu).</summary>
    public string Id { get; }

    /// <summary>Adresy, na kterých má instance podle konfigurace naslouchat.</summary>
    public IReadOnlyList<string> ConfiguredUrls { get; }

    /// <summary>Sestaví identitu z konfigurace aplikace.</summary>
    /// <param name="configuration">Konfigurace (appsettings, proměnné prostředí, argumenty příkazové řádky).</param>
    /// <param name="machineName">Jméno stroje; výchozí je <see cref="Environment.MachineName"/>.</param>
    /// <param name="processId">ID procesu pro případ, že port nelze určit (např. port 0); výchozí je aktuální proces.</param>
    public static InstanceIdentity FromConfiguration(
        IConfiguration configuration,
        string? machineName = null,
        int? processId = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var urls = ResolveConfiguredUrls(configuration);
        var configuredId = configuration[$"{SectionName}:Id"]?.Trim();

        var id = string.IsNullOrEmpty(configuredId)
            ? BuildDefaultId(urls, machineName ?? Environment.MachineName, processId ?? Environment.ProcessId)
            : configuredId;

        return new InstanceIdentity(id, urls);
    }

    /// <summary>
    /// Vrátí adresy, na kterých server skutečně naslouchá. Dokud server neběží
    /// (nebo jde o testovací server bez adres), vrátí adresy z konfigurace.
    /// </summary>
    public IReadOnlyList<string> GetListeningUrls(IServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;

        return addresses is { Count: > 0 } ? addresses.ToArray() : ConfiguredUrls;
    }

    /// <summary>
    /// Vytáhne port z adresy typu <c>http://localhost:5258</c>, <c>http://*:8080</c> nebo <c>http://[::1]:5000</c>.
    /// Bez uvedeného portu vrátí výchozí port schématu (80 / 443). Neplatnou adresu vyhodnotí jako <c>null</c>.
    /// </summary>
    public static int? TryGetPort(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        Match match;

        try
        {
            match = AddressPattern.Match(address);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        if (!match.Success || string.Equals(match.Groups["host"].Value, "unix", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (match.Groups["port"].Success)
        {
            var parsed = int.TryParse(match.Groups["port"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var port);

            return parsed && port <= 65535 ? port : null;
        }

        var scheme = match.Groups["scheme"].Value;

        if (string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return 443;
        }

        return string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) ? 80 : null;
    }

    private static string BuildDefaultId(IReadOnlyList<string> urls, string machineName, int processId)
    {
        var port = urls.Count > 0 ? TryGetPort(urls[0]) : null;

        // Port 0 = systém přidělí náhodný port až při startu; pak je jediným stálým rozlišením ID procesu.
        return port is > 0
            ? $"{machineName}-{port.Value.ToString(CultureInfo.InvariantCulture)}"
            : $"{machineName}-{processId.ToString(CultureInfo.InvariantCulture)}";
    }

    private static IReadOnlyList<string> ResolveConfiguredUrls(IConfiguration configuration)
    {
        // 1) ASPNETCORE_URLS, DOTNET_URLS, --urls, klíč "urls" v appsettings.
        var urls = Split(configuration["urls"]).ToList();

        if (urls.Count > 0)
        {
            return urls;
        }

        // 2) ASPNETCORE_HTTP_PORTS / ASPNETCORE_HTTPS_PORTS (např. Docker image .NET 8+).
        urls.AddRange(Split(configuration["http_ports"]).Select(port => $"http://*:{port}"));
        urls.AddRange(Split(configuration["https_ports"]).Select(port => $"https://*:{port}"));

        if (urls.Count > 0)
        {
            return urls;
        }

        // 3) Kestrel:Endpoints:<název>:Url
        urls.AddRange(configuration.GetSection("Kestrel:Endpoints").GetChildren()
            .Select(endpoint => endpoint["Url"])
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!.Trim()));

        // 4) Výchozí adresa Kestrelu.
        return urls.Count > 0 ? urls : new[] { DefaultUrl };
    }

    private static IEnumerable<string> Split(string? value) =>
        (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
