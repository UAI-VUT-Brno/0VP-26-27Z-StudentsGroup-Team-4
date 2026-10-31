namespace UAI.ZeroVP.DTO;

/// <summary>
/// Úložiště, které instance používá. Neobsahuje žádná tajemství – jen druh úložiště a cestu k souboru.
/// Dvě instance se stejným <see cref="Location"/> a <see cref="Shared"/> = <c>true</c> sdílejí data.
/// </summary>
public sealed class StorageInfoDto
{
    /// <summary>Druh úložiště: <c>InMemory</c>, <c>JsonFile</c> (jedna instance) nebo <c>SharedJsonFile</c> (více instancí).</summary>
    public required string Type { get; init; }

    /// <summary>Přežijí data restart procesu?</summary>
    public required bool Persistent { get; init; }

    /// <summary>Sdílí úložiště více souběžných instancí?</summary>
    public required bool Shared { get; init; }

    /// <summary>Úplná cesta k souboru s daty; <c>null</c> u úložiště jen v paměti.</summary>
    public string? Location { get; init; }
}