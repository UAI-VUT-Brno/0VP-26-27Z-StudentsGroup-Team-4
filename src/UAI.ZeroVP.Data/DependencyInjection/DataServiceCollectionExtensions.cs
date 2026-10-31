using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.Data.Persistence;

namespace UAI.ZeroVP.Data.DependencyInjection;

/// <summary>
/// Registrace datové vrstvy do kontejneru služeb.
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Zaregistruje úložiště úkolů a podpůrné služby persistence jako singleton:
    /// paměťové úložiště s JSON snapshotem, nebo při <c>JsonPersistence:Shared=true</c> sdílený JSON soubor.
    /// </summary>
    public static IServiceCollection AddInMemoryTodoData(
        this IServiceCollection services,
        Action<InMemoryTodoOptions>? configureTodo = null,
        Action<JsonPersistenceOptions>? configurePersistence = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Nastavení se sestaví až při prvním použití z hotové konfigurace, takže vidí i hodnoty
        // přidané po CreateBuilder (např. testovací hostitel).
        services.AddSingleton<InMemoryTodoOptions>(_ =>
        {
            var todoOptions = new InMemoryTodoOptions();
            configureTodo?.Invoke(todoOptions);

            return todoOptions;
        });

        services.AddSingleton<JsonPersistenceOptions>(_ =>
        {
            var persistenceOptions = new JsonPersistenceOptions();
            configurePersistence?.Invoke(persistenceOptions);

            return persistenceOptions;
        });

        services.AddSingleton<IFileStorage, PhysicalFileStorage>();
        services.AddSingleton<TodoStorageDescriptor>(sp => TodoStorageDescriptor.From(sp.GetRequiredService<JsonPersistenceOptions>()));

        services.AddSingleton<ITodoRepository>(sp =>
        {
            var persistenceOptions = sp.GetRequiredService<JsonPersistenceOptions>();

            // Sdílený režim: soubor je zdrojem pravdy pro víc instancí. Jinak platí původní
            // paměťové úložiště s načtením při startu a uložením při ukončení.
            if (persistenceOptions.Enabled && persistenceOptions.Shared)
            {
                return new SharedJsonFileTodoRepository(
                    persistenceOptions,
                    sp.GetRequiredService<InMemoryTodoOptions>(),
                    sp.GetService<TimeProvider>(),
                    sp.GetService<ILogger<SharedJsonFileTodoRepository>>());
            }

            return new InMemoryTodoRepository(
                sp.GetRequiredService<InMemoryTodoOptions>(),
                sp.GetService<TimeProvider>(),
                sp.GetRequiredService<IFileStorage>(),
                persistenceOptions,
                sp.GetService<ILogger<InMemoryTodoRepository>>());
        });

        return services;
    }
}