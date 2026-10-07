using System.Collections;

namespace UAI.ZeroVP.API.Logging;

/// <summary>
/// Stav logovacího záznamu s hotovou zprávou a libovolnými pojmenovanými vlastnostmi.
/// <para>
/// Šablony zpráv (<c>"... {Name} ..."</c>) uloží do záznamu jen ty hodnoty, které ve zprávě
/// opravdu jsou. Middleware potřebuje do záznamu přidat i hodnoty, které se do věty
/// nevejdou (TraceId, Route, Result, Error) – k tomu slouží tato třída.
/// </para>
/// </summary>
internal sealed class StructuredLogState : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly List<KeyValuePair<string, object?>> _items = [];

    public StructuredLogState(string message)
    {
        Message = message;
    }

    /// <summary>Čitelná zpráva záznamu.</summary>
    public string Message { get; }

    public int Count => _items.Count;

    public KeyValuePair<string, object?> this[int index] => _items[index];

    /// <summary>Přidá pojmenovanou vlastnost a vrátí tutéž instanci (fluent zápis).</summary>
    public StructuredLogState With(string name, object? value)
    {
        _items.Add(new KeyValuePair<string, object?>(name, value));
        return this;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
