using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.Data.Abstractions;

namespace UAI.ZeroVP.API.HostedServices;

/// <summary>
/// Hosted service that hooks into application startup and shutdown lifecycles
/// to initialize the task repository from file and save tasks before process termination.
/// </summary>
public sealed class TodoPersistenceHostedService : IHostedService
{
    private readonly ITodoRepository _repository;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<TodoPersistenceHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoPersistenceHostedService"/> class.
    /// </summary>
    /// <param name="repository">The task repository.</param>
    /// <param name="lifetime">The application lifetime.</param>
    /// <param name="logger">The logger instance.</param>
    public TodoPersistenceHostedService(
        ITodoRepository repository,
        IHostApplicationLifetime lifetime,
        ILogger<TodoPersistenceHostedService> logger)
    {
        _repository = repository;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing tasks from persistent storage on service startup...");
        await _repository.LoadAsync(cancellationToken);

        // We register an early save callback on ApplicationStopping to safeguard data
        // in case the shutdown sequence is cut short before reaching hosted services.
        _lifetime.ApplicationStopping.Register(OnApplicationStopping);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Final persistence flush:
        // We perform a save here in StopAsync because the HTTP server (Kestrel/TestServer) has already
        // stopped accepting new requests and drained all active in-flight requests. This ensures that
        // any requests completed during graceful shutdown are safely persisted.
        _logger.LogInformation("Application host is stopping. Persisting final tasks to storage in StopAsync...");
        await SaveSafeAsync(cancellationToken);
    }

    private void OnApplicationStopping()
    {
        // Early flush triggered when application shutdown begins.
        // We do not block subsequent saves so that StopAsync can capture in-flight request completions.
        _logger.LogInformation("Application stopping hook triggered. Performing early persistence flush...");
        SaveSafeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task SaveSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SaveAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while persisting tasks during application shutdown.");
        }
    }
}