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
    /// Zaregistruje paměťové úložiště úkolů a podpůrné služby persistence jako singleton.
    /// </summary>
    public static IServiceCollection AddInMemoryTodoData(
        this IServiceCollection services,
        Action<InMemoryTodoOptions>? configureTodo = null,
        Action<JsonPersistenceOptions>? configurePersistence = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var todoOptions = new InMemoryTodoOptions();
        configureTodo?.Invoke(todoOptions);

        var persistenceOptions = new JsonPersistenceOptions();
        configurePersistence?.Invoke(persistenceOptions);

        services.AddSingleton(todoOptions);
        services.AddSingleton(persistenceOptions);
        services.AddSingleton<IFileStorage, PhysicalFileStorage>();

        services.AddSingleton<ITodoRepository>(sp => new InMemoryTodoRepository(
            sp.GetRequiredService<InMemoryTodoOptions>(),
            sp.GetService<TimeProvider>(),
            sp.GetRequiredService<IFileStorage>(),
            sp.GetRequiredService<JsonPersistenceOptions>(),
            sp.GetService<ILogger<InMemoryTodoRepository>>()));

        return services;
    }
}