using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UAI.ZeroVP.API.Logging;

/// <summary>
/// Pravidla pro název logovacího souboru: <c>{prefix}-yyyyMMdd.log</c>, resp.
/// <c>{prefix}-{instanceId}-yyyyMMdd.log</c>, když je zapnuté <see cref="FileLoggingOptions.IncludeInstanceId"/>.
/// Díky ID instance v názvu nezapisuje několik souběžných instancí do téhož souboru.
/// </summary>
public static class LogFileNaming
{
    private const int MaxInstanceIdLength = 80;

    /// <summary>
    /// Vrátí začátek názvu souboru. ID instance se přidá jen při <paramref name="includeInstanceId"/>
    /// a neprázdném ID; jinak zůstává původní prefix (chování jedné instance se nemění).
    /// </summary>
    public static string BuildPrefix(string prefix, string? instanceId, bool includeInstanceId)
    {
        if (!includeInstanceId || string.IsNullOrWhiteSpace(instanceId))
        {
            return prefix;
        }

        return $"{prefix}-{Sanitize(instanceId)}";
    }

    /// <summary>Sestaví název souboru pro daný den (datum v UTC).</summary>
    public static string BuildFileName(string prefix, DateTimeOffset utcNow) =>
        $"{prefix}-{utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log";

    /// <summary>
    /// Rozhodne, zda je soubor denním logem se zadaným prefixem. Na rozdíl od masky <c>{prefix}-*.log</c>
    /// nezamění logy instance <c>api</c> za logy instance <c>api-2</c> – retence tak nemaže cizí soubory.
    /// </summary>
    public static bool IsLogFileName(string fileName, string prefix) =>
        Regex.IsMatch(
            fileName,
            "^" + Regex.Escape(prefix) + @"-\d{8}\.log$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    /// <summary>
    /// Nahradí znaky, které se nehodí do názvu souboru (lomítka, dvojtečky, mezery, diakritika...),
    /// podtržítkem a zkrátí příliš dlouhé ID.
    /// </summary>
    public static string Sanitize(string value)
    {
        var builder = new StringBuilder(Math.Min(value.Length, MaxInstanceIdLength));

        foreach (var character in value.Trim())
        {
            if (builder.Length >= MaxInstanceIdLength)
            {
                break;
            }

            var allowed = character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_' or '.';
            builder.Append(allowed ? character : '_');
        }

        return builder.ToString();
    }
}