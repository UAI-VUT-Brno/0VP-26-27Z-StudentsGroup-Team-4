using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UAI.ZeroVP.API.Controllers;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class InfoControllerTests
{
    private static async Task<InstanceInfoDto> GetInfoAsync(InMemoryTodoRepository repository, string environmentName)
    {
        var environment = new FakeHostEnvironment { EnvironmentName = environmentName };
        var controller = new InfoController(repository, environment, TimeProvider.System);

        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<InstanceInfoDto>(ok.Value);
    }

    [Fact]
    public async Task Get_ReturnsEnvironmentName()
    {
        var repository = new InMemoryTodoRepository(new InMemoryTodoOptions { SeedSampleData = false });

        var info = await GetInfoAsync(repository, "Staging");

        Assert.Equal("Staging", info.Environment);
    }

    [Fact]
    public async Task Get_ReturnsMachineName()
    {
        var repository = new InMemoryTodoRepository(new InMemoryTodoOptions { SeedSampleData = false });

        var info = await GetInfoAsync(repository, "Testing");

        Assert.Equal(System.Environment.MachineName, info.MachineName);
    }

    [Fact]
    public async Task Get_ReportsNumberOfTasksInMemory()
    {
        var repository = new InMemoryTodoRepository(new InMemoryTodoOptions { SeedSampleData = false });
        await repository.AddAsync(new TodoItem { Id = Guid.NewGuid(), Title = "A", CreatedAtUtc = DateTimeOffset.UtcNow });
        await repository.AddAsync(new TodoItem { Id = Guid.NewGuid(), Title = "B", CreatedAtUtc = DateTimeOffset.UtcNow });

        var info = await GetInfoAsync(repository, "Testing");

        Assert.Equal(2, info.TodoCount);
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