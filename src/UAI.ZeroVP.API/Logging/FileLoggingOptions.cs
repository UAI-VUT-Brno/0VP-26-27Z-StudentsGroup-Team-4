namespace UAI.ZeroVP.API.Logging;

/// <summary>
/// Nastavení souborového logu. Čte se ze sekce <c>FileLogging</c> v konfiguraci,
/// v kontejneru ho lze přepsat proměnnými prostředí, např. <c>FileLogging__Directory=/var/log/zerovp</c>.
/// </summary>
public sealed class FileLoggingOptions
{
    /// <summary>Název konfigurační sekce.</summary>
    public const string SectionName = "FileLogging";

    /// <summary>Zapíná a vypíná zápis do souboru (konzolový log zůstává vždy).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Složka s logy. Relativní cesta se vztahuje ke kořeni obsahu aplikace
    /// (při <c>dotnet run</c> je to složka projektu API, v kontejneru <c>/app</c>).
    /// </summary>
    public string Directory { get; set; } = "logs";

    /// <summary>Začátek názvu souboru; výsledný název je <c>{prefix}-yyyyMMdd.log</c> (datum v UTC).</summary>
    public string FileNamePrefix { get; set; } = "zerovp";

    /// <summary>
    /// Vloží ID instance (<c>Instance:Id</c>) do názvu souboru: <c>{prefix}-{instanceId}-yyyyMMdd.log</c>.
    /// Zapněte při souběžném běhu více instancí, aby každá psala do vlastního souboru.
    /// Výchozí <c>false</c> = původní název bez ID instance. Skript <c>scripts/run-instances</c> to zapíná sám.
    /// </summary>
    public bool IncludeInstanceId { get; set; }

    /// <summary>Kolik nejnovějších denních souborů se ponechá; starší se smažou. Hodnota 0 = nemazat nic.</summary>
    public int RetainedFileCount { get; set; } = 14;

    /// <summary>
    /// Maximální počet záznamů čekajících na zápis na disk. Při přeplnění
    /// (disk neodpovídá) se nejnovější záznamy zahazují, aby aplikace neblokovala požadavky.
    /// </summary>
    public int QueueCapacity { get; set; } = 10_000;
}