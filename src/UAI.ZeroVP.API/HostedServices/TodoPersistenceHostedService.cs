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
    private int _saved;

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

        _lifetime.ApplicationStopping.Register(FlushSave);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        FlushSave();
        return Task.CompletedTask;
    }

    private void FlushSave()
    {
        if (Interlocked.Exchange(ref _saved, 1) == 0)
        {
            _logger.LogInformation("Application stopping hook triggered. Persisting tasks to storage...");
            try
            {
                _repository.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while persisting tasks during application shutdown.");
            }
        }
    }
}