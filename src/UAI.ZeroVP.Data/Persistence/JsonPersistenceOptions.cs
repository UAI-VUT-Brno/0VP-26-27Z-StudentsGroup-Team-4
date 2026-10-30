namespace UAI.ZeroVP.Data.Persistence;

/// <summary>
/// Configuration options for the JSON file persistence layer.
/// </summary>
public sealed class JsonPersistenceOptions
{
    /// <summary>Configuration section name in application settings.</summary>
    public const string SectionName = "JsonPersistence";

    /// <summary>Whether saving to disk and loading from disk is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Path to the JSON file where tasks are saved and loaded.</summary>
    public string FilePath { get; set; } = "data/todos.json";

    /// <summary>
    /// Sdílený režim: soubor je jediným zdrojem pravdy pro víc souběžných instancí (procesů).
    /// Každá operace soubor zamkne, přečte, změní a atomicky přepíše, takže změny jedné instance
    /// jsou hned vidět ostatním. Výchozí <c>false</c> = původní chování (načtení při startu, uložení při ukončení).
    /// Zapíná se proměnnou <c>JsonPersistence__Shared=true</c>; všechny instance musí mít stejnou <see cref="FilePath"/>.
    /// </summary>
    public bool Shared { get; set; }

    /// <summary>Nejdéle jak dlouho (ms) se v sdíleném režimu čeká na zámek souboru, který drží jiná instance.</summary>
    public int LockTimeoutMilliseconds { get; set; } = 10_000;
}