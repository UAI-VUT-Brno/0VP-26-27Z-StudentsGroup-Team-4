using Microsoft.Extensions.DependencyInjection.Extensions;

namespace UAI.ZeroVP.API.Instances;

/// <summary>Registrace identity instance do DI.</summary>
public static class InstanceServiceCollectionExtensions
{
    /// <summary>
    /// Zaregistruje <see cref="InstanceIdentity"/> jako singleton. Identita se sestaví až při prvním
    /// použití z hotové konfigurace, takže vidí i hodnoty přidané po <c>CreateBuilder</c>
    /// (např. testovací hostitel). Opakované volání nic nezmění.
    /// </summary>
    public static IServiceCollection AddInstanceIdentity(this IServiceCollection services)
    {
        services.TryAddSingleton(serviceProvider =>
            InstanceIdentity.FromConfiguration(serviceProvider.GetRequiredService<IConfiguration>()));

        return services;
    }
}
