using Microsoft.Extensions.Logging;

namespace UAI.ZeroVP.API.Logging;

/// <summary>Registrace persistentního logování do aplikace – jediné místo, které volá <c>Program.cs</c>.</summary>
public static class ApplicationLoggingExtensions
{
    /// <summary>
    /// Zapne zápis logů do souboru (sekce <c>FileLogging</c>), logování požadavků
    /// (sekce <c>RequestLogging</c>) a logování životního cyklu aplikace.
    /// Konzolový log zůstává beze změny.
    /// </summary>
    public static WebApplicationBuilder AddApplicationLogging(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<RequestLoggingOptions>(
            builder.Configuration.GetSection(RequestLoggingOptions.SectionName));

        builder.Services.AddSingleton<ApplicationLifecycleLogger>();

        var fileOptions = new FileLoggingOptions();
        builder.Configuration.GetSection(FileLoggingOptions.SectionName).Bind(fileOptions);

        if (fileOptions.Enabled)
        {
            var contentRoot = builder.Environment.ContentRootPath;

            // Registrace přes továrnu (ne hotovou instancí): kontejner pak poskytovatele
            // při ukončení aplikace sám zavolá Dispose a ten vyprázdní frontu do souboru.
            builder.Services.AddSingleton<ILoggerProvider>(_ => new JsonFileLoggerProvider(fileOptions, contentRoot));
        }

        return builder;
    }

    /// <summary>
    /// Přidá do pipeline middleware, které loguje HTTP požadavky.
    /// Musí následovat až za <c>UseExceptionHandler()</c>.
    /// </summary>
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestLoggingMiddleware>();
}
