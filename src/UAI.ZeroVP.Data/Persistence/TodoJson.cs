using System.Text.Json;
using System.Text.Json.Serialization;

namespace UAI.ZeroVP.Data.Persistence;

/// <summary>
/// Společné nastavení serializace úkolů do JSON. Používá ho snapshotová persistence
/// i sdílený soubor, takže oba režimy čtou a zapisují stejný formát.
/// </summary>
internal static class TodoJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}