using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UAI.ZeroVP.API.Logging;

namespace UAI.ZeroVP.Tests.Logging;

/// <summary>Middleware logující HTTP požadavky – volané přímo nad <see cref="DefaultHttpContext"/>.</summary>
public sealed class RequestLoggingMiddlewareTests
{
    [Fact]
    public async Task SuccessfulRequest_IsLoggedAsSuccess()
    {
        using var harness = new LogHarness();
        var middleware = CreateMiddleware(harness, context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(CreateContext("GET", "/api/todos"));

        var entry = Assert.Single(harness.Flush());
        Assert.Equal("Information", entry.Str("level"));
        Assert.Equal(2000, entry.Id());
        Assert.Equal("RequestCompleted", entry.Str("event"));
        Assert.Equal("Success", entry.Str("result"));
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("error").ValueKind);
        Assert.Equal("UAI.ZeroVP.API.Logging.RequestLoggingMiddleware", entry.Str("category"));
        Assert.Contains("HTTP GET /api/todos -> 200", entry.Str("message"));

        var props = entry.Props();
        Assert.Equal("GET", props.GetProperty("Method").GetString());
        Assert.Equal("/api/todos", props.GetProperty("Path").GetString());
        Assert.Equal(200, props.GetProperty("StatusCode").GetInt32());
        Assert.Equal("trace-123", props.GetProperty("TraceId").GetString());
        Assert.True(props.GetProperty("DurationMs").GetDouble() >= 0);
    }

    [Fact]
    public async Task ClientError_IsLoggedAsWarningAndFailureWithErrorText()
    {
        using var harness = new LogHarness();
        var middleware = CreateMiddleware(harness, context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(CreateContext("GET", "/favicon.ico"));

        var entry = Assert.Single(harness.Flush());
        Assert.Equal("Warning", entry.Str("level"));
        Assert.Equal(2000, entry.Id());
        Assert.Equal("Failure", entry.Str("result"));
        Assert.Equal("HTTP 404 Not Found", entry.Str("error"));
        Assert.Equal(404, entry.Props().GetProperty("StatusCode").GetInt32());
    }

    [Fact]
    public async Task ServerErrorStatus_IsLoggedAsError()
    {
        using var harness = new LogHarness();
        var middleware = CreateMiddleware(harness, context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(CreateContext("GET", "/api/todos"));

        var entry = Assert.Single(harness.Flush());
        Assert.Equal("Error", entry.Str("level"));
        Assert.Equal("Failure", entry.Str("result"));
        Assert.Equal("HTTP 503 Service Unavailable", entry.Str("error"));
    }

    [Fact]
    public async Task RouteTemplate_IsLogged_SoRequestsCanBeGroupedByEndpoint()
    {
        using var harness = new LogHarness();
        var endpoint = new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("api/todos/{id:guid}"),
            order: 0,
            EndpointMetadataCollection.Empty,
            displayName: "test");
        var middleware = CreateMiddleware(harness, context =>
        {
            context.SetEndpoint(endpoint);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(CreateContext("GET", "/api/todos/5b1c9f0e-2d7a-4c1e-9a52-7f3e8d6b4a10"));

        var entry = Assert.Single(harness.Flush());
        Assert.Equal("api/todos/{id:guid}", entry.Props().GetProperty("Route").GetString());
        Assert.Equal("/api/todos/5b1c9f0e-2d7a-4c1e-9a52-7f3e8d6b4a10", entry.Props().GetProperty("Path").GetString());
    }

    [Fact]
    public async Task UnhandledException_IsLoggedWithErrorInfo_AndRethrown()
    {
        using var harness = new LogHarness();
        var middleware = CreateMiddleware(harness, _ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(CreateContext("GET", "/api/info")));

        var entry = Assert.Single(harness.Flush());
        Assert.Equal("Error", entry.Str("level"));
        Assert.Equal(2001, entry.Id());
        Assert.Equal("RequestFailed", entry.Str("event"));
        Assert.Equal("Failure", entry.Str("result"));
        Assert.Equal("InvalidOperationException: boom", entry.Str("error"));
        Assert.Contains("System.InvalidOperationException: boom", entry.Str("exception"));
        Assert.Equal(500, entry.Props().GetProperty("StatusCode").GetInt32());
    }

    [Fact]
    public async Task ClientDisconnect_IsLoggedAsCancelledWarning_AndRethrown()
    {
        using var harness = new LogHarness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = CreateContext("GET", "/api/todos");
        context.RequestAborted = cts.Token;
        var middleware = CreateMiddleware(harness, _ => throw new OperationCanceledException(cts.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        var entry = Assert.Single(harness.Flush());
        Assert.Equal("Warning", entry.Str("level"));
        Assert.Equal(2002, entry.Id());
        Assert.Equal("RequestAborted", entry.Str("event"));
        Assert.Equal("Cancelled", entry.Str("result"));
        Assert.NotNull(entry.Str("error"));
    }

    [Fact]
    public async Task ExcludedPaths_AreNotLogged_ButSimilarlyNamedPathsAre()
    {
        using var harness = new LogHarness();
        var middleware = CreateMiddleware(harness, _ => Task.CompletedTask, excludedPaths: "/health");

        await middleware.InvokeAsync(CreateContext("GET", "/health"));
        await middleware.InvokeAsync(CreateContext("GET", "/health/ready"));
        await middleware.InvokeAsync(CreateContext("GET", "/healthy"));

        var entry = Assert.Single(harness.Flush());
        Assert.Equal("/healthy", entry.Props().GetProperty("Path").GetString());
    }

    [Fact]
    public async Task QueryString_IsNeverWrittenToTheLog()
    {
        using var harness = new LogHarness();
        var middleware = CreateMiddleware(harness, _ => Task.CompletedTask);

        await middleware.InvokeAsync(CreateContext("GET", "/api/todos", "?token=tajne-heslo-123"));

        harness.Flush();
        Assert.DoesNotContain("tajne-heslo-123", harness.RawText);
        Assert.DoesNotContain("token=", harness.RawText);
    }

    private static RequestLoggingMiddleware CreateMiddleware(
        LogHarness harness,
        RequestDelegate next,
        params string[] excludedPaths) =>
        new(
            next,
            harness.Logger<RequestLoggingMiddleware>(),
            Options.Create(new RequestLoggingOptions { ExcludedPaths = [.. excludedPaths] }),
            TimeProvider.System);

    private static DefaultHttpContext CreateContext(string method, string path, string query = "")
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-123" };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        return context;
    }
}
