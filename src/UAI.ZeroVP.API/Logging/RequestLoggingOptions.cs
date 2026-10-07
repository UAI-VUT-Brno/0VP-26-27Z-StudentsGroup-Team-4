namespace UAI.ZeroVP.API.Logging;

/// <summary>Nastavení logování HTTP požadavků (sekce <c>RequestLogging</c>).</summary>
public sealed class RequestLoggingOptions
{
    /// <summary>Název konfigurační sekce.</summary>
    public const string SectionName = "RequestLogging";

    /// <summary>
    /// Cesty, jejichž požadavky se nelogují (porovnává se začátek cesty po segmentech).
    /// Výchozí hodnota v <c>appsettings.json</c> vynechává <c>/health</c>, který volá
    /// <c>HEALTHCHECK</c> v Dockeru každých pár sekund a log by zahltil.
    /// </summary>
    public List<string> ExcludedPaths { get; set; } = [];
}
