using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UAI.ZeroVP.API.Controllers;
using UAI.ZeroVP.API.Instances;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class InfoControllerTests
{
    private static async Task<InstanceInfoDto> GetInfoAsync(
        InMemoryTodoRepository repository,
        string environmentName,
        InstanceIdentity? instance = null,
        IServer? server = null)
    {
        var environment = new FakeHostEnvironment { EnvironmentName = environmentName };
        var controller = new InfoController(
            repository,
            environment,
            TimeProvider.System,
            instance ?? new InstanceIdentity("test-instance", new[] { "http://localhost:5258" }),
            server ?? new FakeServer());

        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<InstanceInfoDto>(ok.Value);
    }

    private static InMemoryTodoRepository EmptyRepository() =>
        new(new InMemoryTodoOptions { SeedSampleData = false });

    [Fact]
    public async Task Get_ReturnsEnvironmentName()
    {
        var repository = EmptyRepository();

        var info = await GetInfoAsync(repository, "Staging");

        Assert.Equal("Staging", info.Environment);
    }

    [Fact]
    public async Task Get_ReturnsMachineName()
    {
        var repository = EmptyRepository();

        var info = await GetInfoAsync(repository, "Testing");

        Assert.Equal(System.Environment.MachineName, info.MachineName);
    }

    [Fact]
    public async Task Get_ReportsNumberOfTasksInMemory()
    {
        var repository = EmptyRepository();
        await repository.AddAsync(new TodoItem { Id = Guid.NewGuid(), Title = "A", CreatedAtUtc = DateTimeOffset.UtcNow });
        await repository.AddAsync(new TodoItem { Id = Guid.NewGuid(), Title = "B", CreatedAtUtc = DateTimeOffset.UtcNow });

        var info = await GetInfoAsync(repository, "Testing");

        Assert.Equal(2, info.TodoCount);
    }

    [Fact]
    public async Task Get_ReturnsInstanceId()
    {
        var instance = new InstanceIdentity("api-7", new[] { "http://localhost:5264" });

        var info = await GetInfoAsync(EmptyRepository(), "Testing", instance);

        Assert.Equal("api-7", info.InstanceId);
    }

    [Fact]
    public async Task Get_ReturnsActualListeningUrlsAndPort_WhenServerReportsThem()
    {
        // The server reports the real address (e.g. after binding port 0); it wins over the configured one.
        var instance = new InstanceIdentity("api-1", new[] { "http://localhost:0" });
        var server = new FakeServer("http://127.0.0.1:6123");

        var info = await GetInfoAsync(EmptyRepository(), "Testing", instance, server);

        Assert.Equal(new[] { "http://127.0.0.1:6123" }, info.Urls);
        Assert.Equal(6123, info.Port);
    }

    [Fact]
    public async Task Get_FallsBackToConfiguredUrls_WhenServerReportsNone()
    {
        var instance = new InstanceIdentity("api-2", new[] { "http://localhost:5301", "https://localhost:7301" });

        var info = await GetInfoAsync(EmptyRepository(), "Testing", instance, new FakeServer());

        Assert.Equal(new[] { "http://localhost:5301", "https://localhost:7301" }, info.Urls);
        Assert.Equal(5301, info.Port);
    }

    [Fact]
    public async Task Get_TwoInstances_ReportDifferentIdAndPort()
    {
        var first = await GetInfoAsync(
            EmptyRepository(), "Testing", new InstanceIdentity("api-1", new[] { "http://localhost:5258" }));
        var second = await GetInfoAsync(
            EmptyRepository(), "Testing", new InstanceIdentity("api-2", new[] { "http://localhost:5259" }));

        Assert.NotEqual(first.InstanceId, second.InstanceId);
        Assert.Equal(5258, first.Port);
        Assert.Equal(5259, second.Port);
    }

    /// <summary>Server that only reports the given addresses (like Kestrel after it has started).</summary>
    private sealed class FakeServer : IServer
    {
        public FakeServer(params string[] addresses)
        {
            var feature = new FakeAddressesFeature();

            foreach (var address in addresses)
            {
                feature.Addresses.Add(address);
            }

            var features = new FeatureCollection();
            features.Set<IServerAddressesFeature>(feature);
            Features = features;
        }

        public IFeatureCollection Features { get; }

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FakeAddressesFeature : IServerAddressesFeature
    {
        public ICollection<string> Addresses { get; } = new List<string>();

        public bool PreferHostingUrls { get; set; }
    }

    /// <summary>Nahrada za skutecne hostovaci prostredi aplikace.</summary>
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "UAI.ZeroVP.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}