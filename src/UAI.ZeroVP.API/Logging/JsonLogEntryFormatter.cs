using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace UAI.ZeroVP.API.Logging;

/// <summary>
/// Převádí jeden logovací záznam na jeden řádek JSON (formát JSON Lines).
/// Pevná pole jsou na nejvyšší úrovni, ostatní hodnoty ze zprávy jsou v objektu <c>properties</c>.
/// </summary>
internal static class JsonLogEntryFormatter
{
    private const string ResultSuccess = "Success";
    private const string ResultFailure = "Failure";

    // UnsafeRelaxed: diakritika zůstane čitelná (č, ř, ž ...) místo \u010D.
    // Výstup nikdy nevkládáme do HTML, takže to nevadí. Řídicí znaky (nové řádky)
    // se i tak escapují, takže hodnota z požadavku nemůže "podvrhnout" další řádek logu.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Format<TState>(
        DateTimeOffset timestamp,
        string category,
        LogLevel level,
        EventId eventId,
        TState state,
        Exception? exception,
        string message)
    {
        string? result = null;
        string? error = null;
        List<KeyValuePair<string, object?>>? properties = null;

        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
            {
                switch (pair.Key)
                {
                    case "{OriginalFormat}":
                        break; // šablona zprávy – ve výstupu je už hotová zpráva
                    case "Result":
                        result = ToText(pair.Value);
                        break;
                    case "Error":
                        error = ToText(pair.Value);
                        break;
                    default:
                        (properties ??= new List<KeyValuePair<string, object?>>()).Add(pair);
                        break;
                }
            }
        }

        // Výsledek, který událost neuvedla sama, se odvodí: výjimka nebo úroveň Error a výš = Failure.
        result ??= exception is not null || level >= LogLevel.Error ? ResultFailure : ResultSuccess;
        error ??= exception is null ? null : $"{exception.GetType().Name}: {exception.Message}";

        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("timestamp", timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            writer.WriteString("level", level.ToString());
            writer.WriteNumber("eventId", eventId.Id);
            writer.WriteString("event", eventId.Name);   // null se zapíše jako JSON null
            writer.WriteString("result", result);
            writer.WriteString("message", message);
            writer.WriteString("error", error);          // null se zapíše jako JSON null
            writer.WriteString("category", category);

            if (properties is { Count: > 0 })
            {
                writer.WriteStartObject("properties");
                foreach (var (name, value) in properties)
                {
                    writer.WritePropertyName(name);
                    WriteValue(writer, value);
                }

                writer.WriteEndObject();
            }

            if (exception is not null)
            {
                writer.WriteString("exception", exception.ToString());
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // Convert.ToString(null, provider) vrací prázdný řetězec, ne null – a prázdná chyba
    // by zakryla odvození chyby z výjimky i hodnotu null u úspěšných požadavků.
    private static string? ToText(object? value) =>
        value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case double number when double.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            default:
                // Guid, DateTime, výčty, ... – textová podoba nezávislá na jazyku systému.
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
