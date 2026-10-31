using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using UAI.ZeroVP.API.Instances;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.Persistence;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.API.Controllers;

/// <summary>
/// Diagnostický endpoint. Ukazuje, která instance (kontejner, nebo proces na localhostu)
/// požadavek obsloužila – její ID a adresu – a kolik dat má právě ve své paměti.
/// </summary>
[ApiController]
[Route("api/info")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public sealed class InfoController : ControllerBase
{
    private readonly ITodoRepository _repository;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;
    private readonly InstanceIdentity _instance;
    private readonly IServer _server;
    private readonly TodoStorageDescriptor _storage;

    /// <summary>Vytvoří kontroler se závislostmi z DI kontejneru.</summary>
    public InfoController(
        ITodoRepository repository,
        IHostEnvironment environment,
        TimeProvider timeProvider,
        InstanceIdentity instance,
        IServer server,
        TodoStorageDescriptor storage)
    {
        _repository = repository;
        _environment = environment;
        _timeProvider = timeProvider;
        _instance = instance;
        _server = server;
        _storage = storage;
    }

    /// <summary>Vrátí informace o běžící instanci API.</summary>
    /// <response code="200">Popis instance: ID, adresa a port, prostředí, doba běhu, počet úkolů v paměti.</response>
    [HttpGet]
    [ProducesResponseType(typeof(InstanceInfoDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InstanceInfoDto>> Get(CancellationToken cancellationToken)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "neznámá";

        // Process je IDisposable. Bez using by si každý požadavek na /api/info
        // podržel nativní handle procesu až do příštího sběru odpadků.
        using var process = Process.GetCurrentProcess();

        var urls = _instance.GetListeningUrls(_server);

        return Ok(new InstanceInfoDto
        {
            MachineName = Environment.MachineName,
            Environment = _environment.EnvironmentName,
            Version = version,
            UtcNow = _timeProvider.GetUtcNow(),
            Uptime = _timeProvider.GetUtcNow() - process.StartTime.ToUniversalTime(),
            TodoCount = await _repository.CountAsync(cancellationToken),
            InstanceId = _instance.Id,
            Urls = urls,
            Port = InstanceIdentity.TryGetPort(urls.FirstOrDefault()),
            Storage = new StorageInfoDto
            {
                Type = _storage.Type,
                Persistent = _storage.Persistent,
                Shared = _storage.Shared,
                Location = _storage.Location
            }
        });
    }
}