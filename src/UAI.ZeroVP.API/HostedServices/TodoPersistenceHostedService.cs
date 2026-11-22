using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.Data.Abstractions;

namespace UAI.ZeroVP.API.HostedServices;

/// <summary>
/// Hosted service that hooks into application startup and shutdown lifecycles
/// to initialize the task repository from file and save tasks before process termination.
/// <para>
/// Shutdown saves twice, both asynchronously: an early save in <see cref="StoppingAsync"/>, which the
/// host runs at the very beginning of the shutdown, and a final save in <see cref="StopAsync"/>, after
/// the server has drained in-flight requests. The repository serializes the two saves.
/// </para>
/// </summary>
public sealed class TodoPersistenceHostedService : IHostedLifecycleService
{
    private readonly ITodoRepository _repository;
    private readonly ILogger<TodoPersistenceHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoPersistenceHostedService"/> class.
    /// </summary>
    /// <param name="repository">The task repository.</param>
    /// <param name="logger">The logger instance.</param>
    public TodoPersistenceHostedService(
        ITodoRepository repository,
        ILogger<TodoPersistenceHostedService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing tasks from persistent storage on service startup...");
        await _repository.LoadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        // Early flush at the start of the shutdown, as a safeguard in case the shutdown sequence is
        // cut short before StopAsync runs. Awaited, never blocking a thread pool thread.
        _logger.LogInformation("Application is stopping. Performing early persistence flush...");
        await SaveSafeAsync(cancellationToken);
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

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
