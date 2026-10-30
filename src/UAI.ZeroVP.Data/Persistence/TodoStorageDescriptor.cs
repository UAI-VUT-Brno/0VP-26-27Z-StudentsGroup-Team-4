namespace UAI.ZeroVP.Data.Persistence;

/// <summary>
/// Popis úložiště, které instance právě používá. Neobsahuje žádná tajemství
/// (cesta k souboru, žádná hesla ani connection stringy); vystavuje ho <c>GET /api/info</c>.
/// </summary>
/// <param name="Type">Druh úložiště: <see cref="InMemory"/>, <see cref="JsonFile"/> nebo <see cref="SharedJsonFile"/>.</param>
/// <param name="Persistent">Přežijí data restart procesu?</param>
/// <param name="Shared">Sdílí úložiště více instancí současně?</param>
/// <param name="Location">Úplná cesta k souboru, nebo <c>null</c> u čistě paměťového úložiště.</param>
public sealed record TodoStorageDescriptor(string Type, bool Persistent, bool Shared, string? Location)
{
    /// <summary>Jen v paměti procesu.</summary>
    public const string InMemory = "InMemory";

    /// <summary>JSON soubor načtený při startu a uložený při ukončení (jedna instance).</summary>
    public const string JsonFile = "JsonFile";

    /// <summary>JSON soubor sdílený více instancemi, každá operace se zapisuje hned.</summary>
    public const string SharedJsonFile = "SharedJsonFile";

    /// <summary>Odvodí popis z nastavení persistence.</summary>
    public static TodoStorageDescriptor From(JsonPersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return new TodoStorageDescriptor(InMemory, Persistent: false, Shared: false, Location: null);
        }

        var location = Path.GetFullPath(options.FilePath);

        return options.Shared
            ? new TodoStorageDescriptor(SharedJsonFile, Persistent: true, Shared: true, location)
            : new TodoStorageDescriptor(JsonFile, Persistent: true, Shared: false, location);
    }
}