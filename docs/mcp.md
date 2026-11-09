# MCP server (read-only)

The API contains a small [Model Context Protocol](https://modelcontextprotocol.io/) server that lets
MCP clients (MCP Inspector, GitHub Copilot, Gemini, ...) query the ToDo list. It is a classroom exercise
that shows how existing data can be exposed to AI tools - it is **not** a production feature.

> **WARNING - local development only.**
> The MCP endpoint has **no authentication and no authorization**. Anyone who can reach it can read
> the tasks of **all** users. It is **not safe to expose publicly** and must only be used on a local
> machine with test data. That is why it is switched **off** unless you turn it on explicitly (see below).

## What it offers

The server uses the official [C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk)
(`ModelContextProtocol.AspNetCore`) and the Streamable HTTP transport. It runs inside the existing API
process at the route `/mcp`. The tools use the existing `ITodoRepository`: there is no second data
store and the REST API is unchanged.

| Tool | Arguments | Result |
|---|---|---|
| `list_todos` | `isCompleted` (boolean, optional), `userId` (string, optional) | JSON array of tasks, ordered by priority (high first), then by creation time |
| `get_todo` | `id` (GUID string, required) | JSON object with one task; a tool error when the ID is invalid or no such task exists |

- Both tools are **read-only**: no tool creates, changes or deletes a task, and they are announced to
  clients with the MCP `readOnlyHint` annotation.
- `list_todos` filters are optional and can be combined. An omitted filter is not applied. `userId` is
  case-insensitive, like in `GET /api/todos?userId=...`. A filter that matches nothing returns `[]`.
- Tasks have the same JSON shape as in the REST API (`TodoItemDto`: camelCase names, `priority` as a string).

Example result of `get_todo`:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Write a Dockerfile",
  "description": "Prepare a Dockerfile for the ToDo API.",
  "priority": "High",
  "isCompleted": false,
  "createdAtUtc": "2026-11-06T08:18:26+00:00",
  "completedAtUtc": null,
  "userId": "anna",
  "dueAtUtc": null
}
```

## Turning it on and off

The endpoint is mapped only when the setting `Mcp:Enabled` is `true`.

| Environment | `Mcp:Enabled` | Where it comes from |
|---|---|---|
| Development (`dotnet run`, launch profiles `http` / `https`) | `true` | `appsettings.Development.json` |
| Production, Docker, `docker compose` | `false` | `appsettings.json` |

You can override it with an environment variable, for example `Mcp__Enabled=true` (or `false` to turn it
off in Development). When the endpoint is enabled, the application writes a warning to the log at
startup. When it is disabled, `/mcp` answers `404` like any unknown route.

## Run it locally

```powershell
dotnet run --project src/UAI.ZeroVP.API --launch-profile http
```

The MCP endpoint is then at `http://localhost:5258/mcp`. In Development the application seeds sample
tasks (`Todo:SeedSampleData`), so there is something to list.

## Manual check with MCP Inspector

[MCP Inspector](https://github.com/modelcontextprotocol/inspector) needs Node.js.

1. Start the API as shown above.
2. Start the Inspector: `npx @modelcontextprotocol/inspector`
3. In the Inspector set **Transport Type** to `Streamable HTTP` and **URL** to `http://localhost:5258/mcp`, then click **Connect**.
4. Open the **Tools** tab and click **List Tools**: `list_todos` and `get_todo` appear.
5. Run `list_todos` without arguments, then with `isCompleted = false` and with `userId = anna`.
6. Copy an `id` from the result and run `get_todo` with it. Then run `get_todo` with a random GUID and see the error.

## Use it from Copilot or Gemini

Start the API first. The exact configuration depends on the version of the client, so check its documentation if a setting differs.

**GitHub Copilot in VS Code** - create `.vscode/mcp.json`, start the server from the file, then use Agent mode:

```json
{
  "servers": {
    "todo-api": {
      "type": "http",
      "url": "http://localhost:5258/mcp"
    }
  }
}
```

**Gemini CLI** - add the server with `gemini mcp add --transport http todo-api http://localhost:5258/mcp`
or put it into `~/.gemini/settings.json`:

```json
{
  "mcpServers": {
    "todo-api": {
      "httpUrl": "http://localhost:5258/mcp"
    }
  }
}
```

Then ask for example: *"List my open tasks for user anna"* or *"Show the task with ID ..."*.
The client should call `list_todos` / `get_todo`.

## Automated tests

`McpServerIntegrationTests` starts the real API as a separate process on a free local port, fills it with
120 tasks through the REST API, connects with the official MCP client over HTTP, lists the tools and calls
each of them (all filters, a missing task, an invalid ID). `McpDisabledByDefaultTests` checks that `/mcp`
does not exist when `Mcp:Enabled` is not set.

```powershell
dotnet test --filter "Category=Mcp"
```

The API must be built first (a normal `dotnet build` or `dotnet test` does that).

## Limits

- No authentication, no authorization, no per-user isolation - see the warning at the top.
- Read-only on purpose: writing tools would need authorization and a stricter input validation.
- `list_todos` returns all matching tasks without paging; it is meant for test data of a reasonable size.
- The server is stateless: each request stands on its own and the server never calls the client back.
