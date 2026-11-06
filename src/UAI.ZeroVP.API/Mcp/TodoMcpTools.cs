using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using UAI.ZeroVP.API.Mapping;
using UAI.ZeroVP.Data.Abstractions;

namespace UAI.ZeroVP.API.Mcp;

/// <summary>
/// Read-only MCP tools that expose the ToDo queries to MCP clients (MCP Inspector, Copilot, Gemini, ...).
/// <para>
/// The tools are a thin layer over the existing <see cref="ITodoRepository"/>: they read exactly the
/// same data as the REST API and never change it. No other data store is involved.
/// </para>
/// <para>
/// Every tool returns the same JSON shape as the REST API (<c>TodoItemDto</c>, camelCase property names,
/// enums as strings), so a client sees identical tasks regardless of the protocol it uses.
/// </para>
/// <para>
/// There is no authentication or authorization. The tools are meant for local development and
/// classroom exercises with test data only - see <c>docs/mcp.md</c>.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class TodoMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly ITodoRepository _repository;

    /// <summary>Creates the tools with the repository from the DI container.</summary>
    public TodoMcpTools(ITodoRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    /// <summary>Lists tasks, optionally filtered by completion status and owner.</summary>
    /// <param name="isCompleted"><c>true</c> = completed tasks only, <c>false</c> = open tasks only, omitted = both.</param>
    /// <param name="userId">Owner of the tasks (case-insensitive); omitted = tasks of all users.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A JSON array of tasks, ordered by priority (high first) and then by creation time.</returns>
    [McpServerTool(Name = "list_todos", ReadOnly = true, OpenWorld = false)]
    [Description("Lists to-do tasks as a JSON array, ordered by priority (high first) and then by creation time. " +
                 "Both filters are optional and can be combined; a filter that is omitted is not applied. " +
                 "Returns an empty array when no task matches.")]
    public async Task<string> ListTodosAsync(
        [Description("true = only completed tasks, false = only open (not completed) tasks. Omit to get both.")]
        bool? isCompleted = null,
        [Description("Return only the tasks owned by this user ID (case-insensitive), for example 'anna'. Omit to get the tasks of all users.")]
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetAllAsync(isCompleted, userId, cancellationToken);

        return JsonSerializer.Serialize(items.Select(item => item.ToDto()).ToList(), JsonOptions);
    }

    /// <summary>Returns one task by its identifier.</summary>
    /// <param name="id">Task identifier (a GUID).</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The task as a JSON object.</returns>
    /// <exception cref="McpException">The identifier is not a GUID or no such task exists.</exception>
    [McpServerTool(Name = "get_todo", ReadOnly = true, OpenWorld = false)]
    [Description("Returns one to-do task as a JSON object, looked up by its ID. " +
                 "Fails with an error when the ID is not a valid GUID or no task has that ID.")]
    public async Task<string> GetTodoAsync(
        [Description("The task ID: a GUID such as '3fa85f64-5717-4562-b3fc-2c963f66afa6'.")]
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(id, out var todoId))
        {
            throw new McpException($"'{id}' is not a valid task ID. A GUID is expected.");
        }

        var item = await _repository.GetByIdAsync(todoId, cancellationToken);

        if (item is null)
        {
            throw new McpException($"Task '{todoId}' was not found.");
        }

        return JsonSerializer.Serialize(item.ToDto(), JsonOptions);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        // The same settings the REST API uses: web defaults (camelCase) and enums written as strings.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
