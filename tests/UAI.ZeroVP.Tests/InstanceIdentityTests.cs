using Microsoft.Extensions.Configuration;
using UAI.ZeroVP.API.Instances;

namespace UAI.ZeroVP.Tests;

/// <summary>Instance id and port are taken from configuration; no port is hardcoded.</summary>
public class InstanceIdentityTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private static InstanceIdentity Create(params (string Key, string? Value)[] values) =>
        InstanceIdentity.FromConfiguration(Config(values), machineName: "HOST", processId: 4242);

    [Fact]
    public void ConfiguredId_IsUsed_AndTrimmed()
    {
        var identity = Create(("Instance:Id", "  node-A "), ("urls", "http://localhost:5301"));

        Assert.Equal("node-A", identity.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankConfiguredId_FallsBackToDerivedId(string? configured)
    {
        var identity = Create(("Instance:Id", configured), ("urls", "http://localhost:5301"));

        Assert.Equal("HOST-5301", identity.Id);
    }

    [Fact]
    public void DefaultId_IsMachineNameAndPort()
    {
        var identity = Create(("urls", "http://localhost:5301"));

        Assert.Equal("HOST-5301", identity.Id);
        Assert.Equal(new[] { "http://localhost:5301" }, identity.ConfiguredUrls);
    }

    [Fact]
    public void DefaultId_UsesPortOfTheFirstUrl()
    {
        var identity = Create(("urls", "http://localhost:5301;https://localhost:7301"));

        Assert.Equal("HOST-5301", identity.Id);
        Assert.Equal(2, identity.ConfiguredUrls.Count);
    }

    [Fact]
    public void TwoInstances_WithDifferentPorts_GetDifferentIds()
    {
        var first = Create(("urls", "http://localhost:5258"));
        var second = Create(("urls", "http://localhost:5259"));

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void HttpPorts_AreUsedWhenUrlsAreMissing()
    {
        var identity = Create(("http_ports", "8085;8086"));

        Assert.Equal("HOST-8085", identity.Id);
        Assert.Equal(new[] { "http://*:8085", "http://*:8086" }, identity.ConfiguredUrls);
    }

    [Fact]
    public void KestrelEndpoint_IsUsedWhenNothingElseIsSet()
    {
        var identity = Create(("Kestrel:Endpoints:Http:Url", "http://localhost:5400"));

        Assert.Equal("HOST-5400", identity.Id);
    }

    [Fact]
    public void Urls_TakePrecedenceOverHttpPorts()
    {
        var identity = Create(("urls", "http://localhost:5301"), ("http_ports", "8085"));

        Assert.Equal("HOST-5301", identity.Id);
    }

    [Fact]
    public void WithoutAnyConfiguration_UsesKestrelDefaultPort()
    {
        var identity = Create();

        Assert.Equal("HOST-5000", identity.Id);
        Assert.Equal(new[] { InstanceIdentity.DefaultUrl }, identity.ConfiguredUrls);
    }

    [Fact]
    public void PortZero_FallsBackToProcessId()
    {
        // Port 0 = the OS picks a free port at startup, so it cannot be part of a stable id.
        var identity = Create(("urls", "http://localhost:0"));

        Assert.Equal("HOST-4242", identity.Id);
    }

    [Theory]
    [InlineData("http://localhost:5258", 5258)]
    [InlineData("http://127.0.0.1:5259", 5259)]
    [InlineData("http://*:8081", 8081)]
    [InlineData("http://+:8082", 8082)]
    [InlineData("http://0.0.0.0:8084", 8084)]
    [InlineData("http://[::1]:8083", 8083)]
    [InlineData("https://localhost", 443)]
    [InlineData("http://localhost", 80)]
    [InlineData("HTTP://LOCALHOST:5260", 5260)]
    public void TryGetPort_ParsesCommonAddressForms(string address, int expected)
    {
        Assert.Equal(expected, InstanceIdentity.TryGetPort(address));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("http://localhost:99999")]
    [InlineData("http://unix:/tmp/app.sock")]
    public void TryGetPort_ReturnsNull_ForUnusableAddress(string? address)
    {
        Assert.Null(InstanceIdentity.TryGetPort(address));
    }

    [Fact]
    public void Constructor_RejectsBlankId()
    {
        Assert.ThrowsAny<ArgumentException>(() => new InstanceIdentity(" ", new[] { "http://localhost:5258" }));
    }
}
