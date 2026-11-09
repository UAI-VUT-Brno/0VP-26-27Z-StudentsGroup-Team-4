namespace UAI.ZeroVP.API.Mcp;

/// <summary>
/// Registration of the read-only MCP server (<see cref="TodoMcpTools"/>) and its endpoint.
/// <para>
/// <b>Local development only.</b> The endpoint has no authentication or authorization and exposes
/// every task of every user. It is therefore mapped only when <c>Mcp:Enabled</c> is <c>true</c>:
/// <c>appsettings.Development.json</c> turns it on, <c>appsettings.json</c> keeps it off, so
/// Production (including the Docker image) does not serve it unless somebody opts in explicitly.
/// </para>
/// </summary>
public static class TodoMcpServerExtensions
{
    /// <summary>Configuration key that switches the MCP endpoint on (<c>Mcp__Enabled=true</c> as an environment variable).</summary>
    public const string EnabledSetting = "Mcp:Enabled";

    /// <summary>Route of the MCP endpoint (Streamable HTTP transport).</summary>
    public const string EndpointPath = "/mcp";

    /// <summary>
    /// Registers the MCP server with the Streamable HTTP transport and the ToDo tools.
    /// Registering is harmless on its own: nothing is reachable until <see cref="MapTodoMcpServer"/> maps the endpoint.
    /// </summary>
    public static IServiceCollection AddTodoMcpServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                // Stateless: every request stands on its own, no session is kept on the server.
                // The tools only read data and never need to call the client back.
                options.Stateless = true;
            })
            .WithTools<TodoMcpTools>();

        return services;
    }

    /// <summary>
    /// Maps the MCP endpoint at <see cref="EndpointPath"/> when <see cref="EnabledSetting"/> is <c>true</c>.
    /// Otherwise nothing is mapped and the path answers 404 like any unknown route.
    /// </summary>
    public static WebApplication MapTodoMcpServer(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Configuration.GetValue(EnabledSetting, false))
        {
            return app;
        }

        // Kept out of the OpenAPI document: the MCP endpoint is not part of the REST API.
        app.MapMcp(EndpointPath).ExcludeFromDescription();

        app.Logger.LogWarning(
            "MCP server is enabled at {Path} (read-only tools list_todos and get_todo). " +
            "It has NO authentication and exposes the tasks of all users - for local development with test data only. " +
            "Do not expose it publicly. Set {Setting}=false to turn it off.",
            EndpointPath,
            EnabledSetting);

        return app;
    }
}
