using System.Globalization;
using System.Text;

namespace UAI.ZeroVP.API.Logging;

/// <summary>
/// Úprava textu od uživatele (např. název úkolu) před vložením do zprávy logu.
/// <para>
/// Konzolový (textový) log vypisuje zprávu tak, jak je. Znak nového řádku v hodnotě by tam
/// založil nový, podvržený řádek logu (CWE-117, log forging). Řídicí znaky se proto nahradí
/// viditelnou escape sekvencí: <c>\r</c>, <c>\n</c>, <c>\t</c>, ostatní jako <c>\uXXXX</c>.
/// Souborový JSON log escapuje hodnoty sám; tahle úprava chrání textové výstupy.
/// </para>
/// <para>
/// Upravuje se jen hodnota pro log – uložená data (úkol v úložišti, odpověď API) zůstávají beze změny.
/// </para>
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Vrátí hodnotu bez znaků, které by v textovém logu zalomily řádek nebo skryly text.
    /// Hodnota bez takových znaků se vrátí beze změny (stejná instance).
    /// </summary>
    public static string? ForLog(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.Any(NeedsEscaping))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 16);

        foreach (var character in value)
        {
            switch (character)
            {
                case '\r':
                    builder.Append(@"\r");
                    break;
                case '\n':
                    builder.Append(@"\n");
                    break;
                case '\t':
                    builder.Append(@"\t");
                    break;
                default:
                    if (NeedsEscaping(character))
                    {
                        builder.Append(@"\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    // Řídicí znaky (včetně U+0085 NEL) a Unicode oddělovače řádku a odstavce, které některé
    // prohlížeče logů také zalomí.
    private static bool NeedsEscaping(char character) =>
        char.IsControl(character) || character is '\x2028' or '\x2029';
}
