using System.Globalization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace UAI.ZeroVP.API.Logging;

/// <summary>
/// Zapíše do logu každý HTTP požadavek: metodu, cestu, stavový kód, dobu zpracování
/// a výsledek (<c>Success</c>, <c>Failure</c> nebo <c>Cancelled</c>).
/// <para>
/// Middleware musí být zařazený <b>za</b> <c>UseExceptionHandler()</c>. Díky tomu vidí
/// neošetřené výjimky dřív, než je handler přemění na odpověď 500, a zapíše je
/// i s popisem chyby.
/// </para>
/// <para>
/// Záměrně se nelogují query string, hlavičky, tělo požadavku ani IP adresa klienta –
/// mohou obsahovat osobní nebo citlivé údaje.
/// </para>
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private const int ClientClosedRequest = 499;

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly PathString[] _excludedPaths;

    /// <summary>Vytvoří middleware se závislostmi z DI kontejneru.</summary>
    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger,
        IOptions<RequestLoggingOptions> options,
        TimeProvider timeProvider)
    {
        _next = next;
        _logger = logger;
        _timeProvider = timeProvider;
        _excludedPaths = options.Value.ExcludedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new PathString(path.StartsWith('/') ? path : "/" + path))
            .ToArray();
    }

    /// <summary>Zpracuje požadavek a zapíše o něm záznam.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExcluded(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var startTimestamp = _timeProvider.GetTimestamp();

        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Klient se odpojil. Není to chyba aplikace, proto jen varování.
            Write(context, startTimestamp, ClientClosedRequest, LogLevel.Warning, LogEvents.RequestAborted,
                result: "Cancelled", error: "Klient zrušil požadavek před dokončením.", exception: null);
            throw;
        }
        catch (Exception exception)
        {
            // Odpověď 500 zatím nikdo nenastavil – to udělá až UseExceptionHandler výš v pipeline.
            Write(context, startTimestamp, StatusCodes.Status500InternalServerError, LogLevel.Error, LogEvents.RequestFailed,
                result: "Failure", error: null, exception: exception);
            throw;
        }

        var statusCode = context.Response.StatusCode;
        var level = statusCode >= 500 ? LogLevel.Error
            : statusCode >= 400 ? LogLevel.Warning
            : LogLevel.Information;
        var error = statusCode >= 400
            ? $"HTTP {statusCode} {ReasonPhrases.GetReasonPhrase(statusCode)}"
            : null;

        Write(context, startTimestamp, statusCode, level, LogEvents.RequestCompleted,
            result: error is null ? "Success" : "Failure", error: error, exception: null);
    }

    private bool IsExcluded(PathString path)
    {
        foreach (var excluded in _excludedPaths)
        {
            if (path.StartsWithSegments(excluded))
            {
                return true;
            }
        }

        return false;
    }

    private void Write(
        HttpContext context,
        long startTimestamp,
        int statusCode,
        LogLevel level,
        EventId eventId,
        string result,
        string? error,
        Exception? exception)
    {
        var durationMs = Math.Round(_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds, 1);
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "/";
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;

        var message = string.Create(CultureInfo.InvariantCulture,
            $"HTTP {method} {path} -> {statusCode} za {durationMs:0.0} ms");

        var state = new StructuredLogState(message)
            .With("Result", result)
            .With("Error", error)
            .With("Method", method)
            .With("Path", path)
            .With("Route", route)
            .With("StatusCode", statusCode)
            .With("DurationMs", durationMs)
            .With("TraceId", context.TraceIdentifier);

        _logger.Log(level, eventId, state, exception, FormatMessage);
    }

    private static string FormatMessage(StructuredLogState state, Exception? exception) => state.Message;
}
