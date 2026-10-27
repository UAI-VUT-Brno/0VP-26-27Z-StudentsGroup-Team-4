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
}