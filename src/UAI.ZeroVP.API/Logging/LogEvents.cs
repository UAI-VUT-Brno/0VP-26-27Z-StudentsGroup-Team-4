using Microsoft.Extensions.Logging;

namespace UAI.ZeroVP.API.Logging;

/// <summary>
/// Katalog identifikátorů událostí, které aplikace zapisuje do logu.
/// <para>
/// Číselné rozsahy: 1xxx = životní cyklus aplikace, 2xxx = HTTP požadavky,
/// 3xxx = operace nad úkoly. Čísla jsou součástí dokumentace (docs/logging.md),
/// proto je neměňte – nové události přidávejte s novým číslem.
/// </para>
/// </summary>
public static class LogEvents
{
    // --- 1xxx: životní cyklus aplikace -------------------------------------

    /// <summary>Aplikace byla sestavena a začíná se spouštět.</summary>
    public static readonly EventId ApplicationStarting = new(1000, nameof(ApplicationStarting));

    /// <summary>Server naslouchá a aplikace přijímá požadavky.</summary>
    public static readonly EventId ApplicationStarted = new(1001, nameof(ApplicationStarted));

    /// <summary>Byl vyžádán konec (Ctrl+C, SIGTERM, docker stop) – aplikace se zastavuje.</summary>
    public static readonly EventId ApplicationStopping = new(1002, nameof(ApplicationStopping));

    /// <summary>Aplikace se korektně zastavila.</summary>
    public static readonly EventId ApplicationStopped = new(1003, nameof(ApplicationStopped));

    /// <summary>Start nebo běh aplikace skončil neošetřenou chybou.</summary>
    public static readonly EventId ApplicationFailed = new(1004, nameof(ApplicationFailed));

    // --- 2xxx: HTTP požadavky ----------------------------------------------

    /// <summary>Požadavek byl zpracován a odpověď odeslána (úspěšným i chybovým stavovým kódem).</summary>
    public static readonly EventId RequestCompleted = new(2000, nameof(RequestCompleted));

    /// <summary>Zpracování požadavku skončilo neošetřenou výjimkou (HTTP 500).</summary>
    public static readonly EventId RequestFailed = new(2001, nameof(RequestFailed));

    /// <summary>Klient zrušil požadavek dřív, než byl zpracován.</summary>
    public static readonly EventId RequestAborted = new(2002, nameof(RequestAborted));

    // --- 3xxx: operace nad úkoly -------------------------------------------

    /// <summary>Byl vytvořen nový úkol.</summary>
    public static readonly EventId TodoCreated = new(3000, nameof(TodoCreated));

    /// <summary>Úkol byl smazán.</summary>
    public static readonly EventId TodoDeleted = new(3001, nameof(TodoDeleted));

    /// <summary>A weekly sequence of tasks was created (POST /api/todos/repeat).</summary>
    public static readonly EventId WeeklyTodosCreated = new(3002, nameof(WeeklyTodosCreated));
}
