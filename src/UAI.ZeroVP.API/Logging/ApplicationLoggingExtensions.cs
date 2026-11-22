using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UAI.ZeroVP.API.Instances;

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

        // ID instance je potřeba už při vzniku poskytovatele (název souboru).
        builder.Services.AddInstanceIdentity();

        var contentRoot = builder.Environment.ContentRootPath;

        // Registrace přes továrnu (ne hotovou instancí): kontejner pak poskytovatele
        // při ukončení aplikace sám zavolá Dispose a ten vyprázdní frontu do souboru.
        // Celé nastavení, včetně FileLogging:Enabled, se čte až tady z hotové konfigurace.
        // Při čtení už během sestavování builderu by se neprojevilo přepsání přidané později
        // (ConfigureAppConfiguration v testech nebo při hostování). ID instance určuje název souboru.
        builder.Services.AddSingleton<ILoggerProvider>(serviceProvider =>
        {
            var options = new FileLoggingOptions();
            serviceProvider.GetRequiredService<IConfiguration>()
                .GetSection(FileLoggingOptions.SectionName).Bind(options);

            if (!options.Enabled)
            {
                return NullLoggerProvider.Instance;
            }

            var instance = serviceProvider.GetRequiredService<InstanceIdentity>();

            return new JsonFileLoggerProvider(options, contentRoot, instanceId: instance.Id);
        });

        return builder;
    }

    /// <summary>
    /// Přidá do pipeline middleware, které loguje HTTP požadavky.
    /// Musí následovat až za <c>UseExceptionHandler()</c>.
    /// </summary>
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestLoggingMiddleware>();
}
