namespace UAI.ZeroVP.DTO;

/// <summary>
/// Informace o běžící instanci API.
/// Ve výuce Dockeru slouží k tomu, aby bylo vidět, která replika kontejneru
/// požadavek obsloužila a že každá má vlastní (prázdnou) paměť.
/// </summary>
public sealed record InstanceInfoDto
{
    /// <summary>Jméno stroje – v kontejneru odpovídá jeho hostname (zkrácené ID kontejneru).</summary>
    public required string MachineName { get; init; }

    /// <summary>Název prostředí ASP.NET Core (Development / Production / ...).</summary>
    public required string Environment { get; init; }

    /// <summary>Verze sestavení API.</summary>
    public required string Version { get; init; }

    /// <summary>Aktuální čas instance v UTC.</summary>
    public required DateTimeOffset UtcNow { get; init; }

    /// <summary>Doba běhu procesu – po restartu kontejneru se vynuluje.</summary>
    public required TimeSpan Uptime { get; init; }

    /// <summary>Počet úkolů, které má tato instance právě v paměti.</summary>
    public required int TodoCount { get; init; }

    /// <summary>
    /// Identifikátor instance. Zadává se konfigurací (<c>Instance__Id</c>); jinak je odvozen
    /// ze jména stroje a portu, např. <c>PC-5258</c>. Odlišuje souběžně běžící instance.
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>Adresy, na kterých instance naslouchá (např. <c>http://localhost:5258</c>).</summary>
    public required IReadOnlyList<string> Urls { get; init; }

    /// <summary>Port první adresy z <see cref="Urls"/>; <c>null</c>, pokud ho nelze určit.</summary>
    public int? Port { get; init; }

    /// <summary>
    /// Úložiště, které instance používá (druh, sdílené, cesta k souboru; bez tajemství).
    /// Instance s totožným <c>storage.location</c> a <c>storage.shared</c> = <c>true</c> vidí stejná data.
    /// </summary>
    public StorageInfoDto? Storage { get; init; }
}