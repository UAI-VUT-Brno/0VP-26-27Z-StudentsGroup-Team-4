using Microsoft.Extensions.DependencyInjection;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.DependencyInjection;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.Data.Persistence;

namespace UAI.ZeroVP.Tests;

/// <summary>Which repository is registered for which persistence settings, and what the storage descriptor says.</summary>
public class SharedStorageRegistrationTests
{
    private static ServiceProvider Build(Action<JsonPersistenceOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddInMemoryTodoData(todo => todo.SeedSampleData = false, configure);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Defaults_UseTheSnapshotRepository_NotTheSharedOne()
    {
        using var provider = Build(_ => { });

        Assert.IsType<InMemoryTodoRepository>(provider.GetRequiredService<ITodoRepository>());
        Assert.False(new JsonPersistenceOptions().Shared);
    }

    [Fact]
    public void Defaults_DescribeTheSingleInstanceJsonFile()
    {
        using var provider = Build(_ => { });

        var descriptor = provider.GetRequiredService<TodoStorageDescriptor>();

        Assert.Equal(TodoStorageDescriptor.JsonFile, descriptor.Type);
        Assert.True(descriptor.Persistent);
        Assert.False(descriptor.Shared);
    }

    [Fact]
    public void SharedMode_RegistersTheSharedRepository_AndDescribesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "zerovp-di", "todos.json");
        using var provider = Build(options =>
        {
            options.Shared = true;
            options.FilePath = path;
        });

        var repository = provider.GetRequiredService<ITodoRepository>();
        var descriptor = provider.GetRequiredService<TodoStorageDescriptor>();

        Assert.IsType<SharedJsonFileTodoRepository>(repository);
        Assert.Same(repository, provider.GetRequiredService<ITodoRepository>());
        Assert.Equal(TodoStorageDescriptor.SharedJsonFile, descriptor.Type);
        Assert.True(descriptor.Persistent);
        Assert.True(descriptor.Shared);
        Assert.Equal(Path.GetFullPath(path), descriptor.Location);
    }

    [Fact]
    public void DisabledPersistence_WinsOverSharedMode()
    {
        using var provider = Build(options =>
        {
            options.Enabled = false;
            options.Shared = true;
        });

        var descriptor = provider.GetRequiredService<TodoStorageDescriptor>();

        Assert.IsType<InMemoryTodoRepository>(provider.GetRequiredService<ITodoRepository>());
        Assert.Equal(TodoStorageDescriptor.InMemory, descriptor.Type);
        Assert.False(descriptor.Persistent);
        Assert.False(descriptor.Shared);
        Assert.Null(descriptor.Location);
    }

    [Fact]
    public void Options_AreBuiltFromTheFinalConfiguration_OnFirstUse_NotAtRegistration()
    {
        var calls = 0;
        var services = new ServiceCollection();
        services.AddInMemoryTodoData(configurePersistence: _ => calls++);

        Assert.Equal(0, calls);

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<JsonPersistenceOptions>();

        Assert.Equal(1, calls);
    }

    [Fact]
    public void SharedMode_HasASensibleLockTimeoutByDefault()
    {
        Assert.Equal(10_000, new JsonPersistenceOptions().LockTimeoutMilliseconds);
    }
}