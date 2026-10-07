using Microsoft.Extensions.DependencyInjection;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.DependencyInjection;
using UAI.ZeroVP.Data.InMemory;

namespace UAI.ZeroVP.Tests;

public class DataRegistrationTests
{
    [Fact]
    public void Options_HaveExpectedDefaults()
    {
        var options = new InMemoryTodoOptions();

        Assert.True(options.SeedSampleData);
        Assert.Equal("Todo", InMemoryTodoOptions.SectionName);
    }

    [Fact]
    public void AddInMemoryTodoData_RegistersRepositoryAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddInMemoryTodoData(o => o.SeedSampleData = false);
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<ITodoRepository>();
        var second = provider.GetRequiredService<ITodoRepository>();

        Assert.IsType<InMemoryTodoRepository>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task AddInMemoryTodoData_ConfigureCallback_DisablesSeed()
    {
        var services = new ServiceCollection();
        services.AddInMemoryTodoData(o => o.SeedSampleData = false);
        using var provider = services.BuildServiceProvider();

        var repository = provider.GetRequiredService<ITodoRepository>();

        Assert.Equal(0, await repository.CountAsync());
    }
}