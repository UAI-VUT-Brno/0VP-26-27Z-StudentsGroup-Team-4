---
name: architecture
description: >
  Strict architectural and style rules for all C# code written by the /issue-to-pr
  C# Implementer (Stage 2): layer boundaries (DTO -> Data -> API), optimistic
  concurrency, ProblemDetails errors, MCP tools, .editorconfig conformance and
  commit discipline. The implementer reads this file before writing any code.
---

# Architecture Rules

Paths in this file are from the repository root; the synchronized Claude copy lives
under `.claude/skills/issue-to-pr/`.

This file is read by the C# Implementer (`skills/issue-to-pr/agents/csharp-implementer.md`) before it
writes or edits any file under `src/` or `tests/`. It describes how this
repository is layered and what generated code must look like. It does **not**
redefine commit conventions or formatting rules - it points to the files that own
them (see §1).

## 1. Binding references (single sources of truth)

| Topic | Owned by | Rule |
|-------|----------|------|
| Formatting, naming, style, encoding | `.editorconfig` (repository root) | Generated code MUST conform. It is never overridden, relaxed or worked around. |
| Commit message format | `skills/issue-to-pr/rules/repo-conventions.md` §2 | Every implementation commit MUST follow it (`feat:`, `test:`, `fix:` ...). |

- Read both files before the first edit. If they disagree with this file, they win.
- Never copy their rules into new files; refer to them by name.
- The conformance gate is `dotnet format UAI.ZeroVP.sln --verify-no-changes` over the
  whole solution, the same command as CI (§8). Code is **not complete** until that
  command exits with code 0.

## 2. Solution layout and dependency direction

```text
src/UAI.ZeroVP.DTO/    contracts, DataAnnotations, enums   (no project references)
src/UAI.ZeroVP.Data/   entities, ITodoRepository, storage  (references DTO)
src/UAI.ZeroVP.API/    controllers, mapping, MCP, pipeline (references DTO, Data)
tests/UAI.ZeroVP.Tests/ xUnit tests                         (references API)
```

- Dependencies flow one way: **DTO -> Data -> API**. Never add a reference in the
  opposite direction and never add a new project reference without the issue
  explicitly requiring it.
- Target `net10.0`, `Nullable` enabled, `ImplicitUsings` enabled (as in the existing
  `.csproj` files). Do not add NuGet packages unless the issue requires it; if one
  is required, pin an explicit version.
- Each layer owns its types: the API never returns an entity, and the DTO layer
  never references `TodoItem`.

## 3. DTO layer (`src/UAI.ZeroVP.DTO`)

- Contracts are `public sealed record` types with `init` properties.
- Validation lives here as DataAnnotations: `[Required(AllowEmptyStrings = false, ...)]`,
  `[StringLength(...)]`, `[Range(...)]`, `[EnumDataType(typeof(...))]`, each with an
  `ErrorMessage`. Follow the existing request records (`CreateTodoItemRequest`,
  `UpdateTodoItemRequest`) for length limits and message style.
- Request `Title`-style strings are `string` with `= string.Empty` and no `required`
  keyword (see the explanation in `CreateTodoItemRequest.Title`), so that the
  DataAnnotations message reaches the client.
- Enums are shared and live here (`TodoPriority`). Give members explicit numeric
  values; never reorder or renumber existing members.
- Output DTOs never expose internal fields (for example `UpdatedAtUtc`).
- Every public type and member has an XML doc comment (`GenerateDocumentationFile`
  feeds OpenAPI/Swagger).
- No logic, no I/O, no reference to any other project.

## 4. Data layer (`src/UAI.ZeroVP.Data`)

- Entities are `public sealed class` types in `Entities/`. They own the internal
  fields. `Clone()` MUST copy every property - when a property is added to
  `TodoItem`, `Clone()` is updated in the same change.
- Storage contracts live in `Abstractions/ITodoRepository`. All methods are async,
  take a `CancellationToken cancellationToken = default` as the last parameter and
  return `Task<...>`.
- Changing `ITodoRepository` means updating **every** implementation in the same
  change: `InMemory/InMemoryTodoRepository` and
  `Persistence/SharedJsonFileTodoRepository.cs` - plus every caller and
  test that uses the changed member. A change that compiles in only one
  implementation is incomplete.
- Registration of new services goes through
  `DependencyInjection/DataServiceCollectionExtensions.cs`.
- Time is never read from `DateTime.UtcNow`; the caller passes `nowUtc` or the
  type depends on `TimeProvider`.

### 4.1 Optimistic concurrency (version checks and increments)

Whenever an issue asks for protection against lost updates, use this pattern.

1. **Entity.** `TodoItem` carries `public int Version { get; set; } = 1;` A newly
   created item starts at version 1. `Clone()` copies it.
2. **The repository owns the version.** Callers send the version they *expect*;
   they never choose the new one. The repository compares the stored version with the
   expected one, and only on a match applies the change and sets the stored
   version to `expected + 1`.
3. **Contract.** The update method takes the expected version and reports one of
   three outcomes, not a bare `bool`:

   ```csharp
   /// <summary>Outcome of an optimistic-concurrency update.</summary>
   public enum TodoUpdateResult
   {
       /// <summary>The item was updated and its version incremented.</summary>
       Updated,

       /// <summary>No item with the given identifier exists.</summary>
       NotFound,

       /// <summary>The stored version differs from the expected one; nothing was changed.</summary>
       VersionConflict
   }
   ```

   ```csharp
   Task<TodoUpdateResult> UpdateAsync(TodoItem item, int expectedVersion, CancellationToken cancellationToken = default);
   ```

4. **Atomic compare-and-swap.** The version check and the write happen in one
   atomic step: `ConcurrentDictionary.TryUpdate(key, newValue, comparisonValue)` or a
   lock in `InMemoryTodoRepository`; inside the existing file lock in the shared
   JSON repository. A read followed by a separate write is a race and is not allowed.
5. **Persistence.** `Version` is part of the persisted JSON. A stored record that has
   no `Version` (data written before this feature) is read as version `1`.
6. **Conflict is never silent.** A mismatch returns `VersionConflict`; the API
   turns it into `409 Conflict` (§5.4). The "last write wins" fallback is forbidden.
   This applies to **every** caller of the update method, not only `PUT`: an action
   without a request body, such as `POST /api/todos/{id}/complete`, passes the
   `Version` of the item it has just read. A change made by someone else between
   that read and the write therefore also ends as `409`.
7. **Contracts.** `TodoItemDto` exposes `Version`; the update request carries the
   expected version (`[Range(1, int.MaxValue, ErrorMessage = "...")]`). It is
   required for protected endpoints - a missing version is a `400`, not a skipped
   check.

## 5. API layer (`src/UAI.ZeroVP.API`)

### 5.1 Controllers

- `[ApiController]`, an explicit `[Route("api/<resource>")]`, `[Produces("application/json")]`;
  controllers are `public sealed class`, named `<Resource>Controller`, in `Controllers/`.
- Constructor injection only, private readonly fields named `_camelCase`. A controller
  depends on `ITodoRepository`, `TimeProvider` and `ILogger<T>` - never on a
  concrete repository.
- Actions are `async`, return `ActionResult<T>` and accept a `CancellationToken`
  as the last parameter. Route constraints are explicit (`{id:guid}`).
- No business rules in controllers beyond orchestration; mapping goes through
  `Mapping/TodoMapper` (extension methods). The mapper never copies `Version` from
  a request onto an entity - the repository increments it.
- Every action documents each status code with `[ProducesResponseType]` **and** an
  XML `<response code="...">` tag. Error codes (`400`, `404`, `409`) use
  `typeof(ProblemDetails)` (`ValidationProblemDetails` for `400`).
- Log through `LogEvents` constants (`Logging/LogEvents.cs`); add a new event id
  there instead of inlining numbers.

### 5.2 Route and verb conventions

| Operation | Verb and route | Success |
|-----------|----------------|---------|
| List / filter | `GET /api/todos` | `200` |
| Detail | `GET /api/todos/{id}` | `200` / `404` |
| Create | `POST /api/todos` | `201` + `Location` (`CreatedAtRoute`) |
| Full update | `PUT /api/todos/{id}` | `200` / `404` / `409` |
| Delete | `DELETE /api/todos/{id}` | `204` / `404` |
| Action on a resource | `POST /api/todos/{id}/<action>` | `200` / `404` |

Never rename or remove an existing route, query parameter or JSON property unless
the issue explicitly requires a breaking change.

### 5.3 Errors are always `ProblemDetails` (RFC 9457)

- Every error response has the `ProblemDetails` shape. `AddProblemDetails()`,
  `UseExceptionHandler()` and `UseStatusCodePages()` are already configured in
  `Program.cs`; do not add a second error format and never return a bare string or an
  anonymous object as an error.
- Validation errors (`400`) come from `[ApiController]` automatically
  (`ValidationProblemDetails`). Do not validate by hand what a DataAnnotation can say.
- Not found uses the existing `NotFoundTodo(id)` helper; reuse it instead of
  creating a parallel one.
- Never leak exception messages, stack traces or file paths in `detail`.

### 5.4 Concurrency conflict response

```csharp
return Problem(
    statusCode: StatusCodes.Status409Conflict,
    title: "Concurrent modification",
    detail: $"Task '{id}' was changed by someone else. Reload it and retry with the current version.");
```

The action also declares `[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]`
and `<response code="409">`.

### 5.5 MCP tools (only when the issue asks for them)

- Tools live in `Mcp/`, in a `[McpServerToolType]` class that depends only on
  `ITodoRepository`; registration goes through `Mcp/TodoMcpServerExtensions.cs`.
- Follow `TodoMcpTools`: `[McpServerTool(Name = "snake_case_name", ReadOnly = true, OpenWorld = false)]`
  plus a `[Description]` on the method and on every parameter.
- Tools are **read-only** unless the issue explicitly asks for a write tool, and then
  `ReadOnly = false` is set deliberately.
- Return the same JSON shape as the REST API (`ToDto()`, camelCase, enums as strings).
  Report bad input by throwing `McpException` with a clear message.
- Add the tool to `docs/mcp.md` only if the issue says so (the implementer has no
  write access outside `src/` and `tests/`; report it under `warnings`).

## 6. Tests (`tests/UAI.ZeroVP.Tests`)

- xUnit (`[Fact]`, `[Theory]`); one test class per unit under test, named
  `<Unit>Tests`; test method names state behaviour.
- New behaviour gets tests: repository behaviour (including `VersionConflict` and
  the version increment), controller status codes and `ProblemDetails` bodies.
- Tests that need disk or a process must be isolated (temp directories); long
  performance tests keep `Category=Performance` so CI can filter them out.

## 7. .editorconfig conformance

The root `.editorconfig` is authoritative. As it stands, for `*.cs` files:

- charset **`utf-8-bom`** - every new `.cs` file starts with a UTF-8 BOM;
- line endings **CRLF** (`end_of_line = crlf`), final newline, no trailing whitespace;
- indent: 4 spaces; `.csproj` / `.props` / `.json` / `.yml`: 2 spaces;
- Security-category analyzer diagnostics are errors.

In addition, new code matches the existing code base: file-scoped namespaces,
`using` directives first and ordered (`System.*` before others), one top-level type
per file named after the type, braces on every block, `_camelCase` private fields,
`PascalCase` for types and members, `Async` suffix on async methods, `sealed` by
default, and XML documentation on public members.

Anything not spelled out here is decided by `.editorconfig`; when it is silent,
copy the style of the neighbouring file. Do not edit `.editorconfig` itself.

## 8. Definition of done

Let `<files>` be every file this run created or changed. Run from the repository root,
in this order; all must succeed:

```bash
dotnet format UAI.ZeroVP.sln --include <files>
dotnet build UAI.ZeroVP.sln --configuration Release
dotnet format UAI.ZeroVP.sln --verify-no-changes --no-restore
```

- The first command normalises (BOM, CRLF, indentation, final newline) **only** the
  files of this run. `dotnet format` in fix mode without `--include` is forbidden:
  it would rewrite files outside the plan.
- The third command is the gate. It checks the **whole solution** with exactly the
  command CI runs (`.github/workflows/ci.yml`, step "Verify formatting"), so a run
  that passes here does not fail the formatting step in CI.
- The baseline on `main` is clean (#60). Before the first edit the implementer runs
  `dotnet format UAI.ZeroVP.sln --verify-no-changes` once, and if that already fails
  it stops with `ERR_BASELINE_NOT_CLEAN` without fixing anything. A violation the
  gate reports after the edits therefore belongs to this run, and it is fixed only
  through the files of this run.
- Analyse failures by diagnostic id (`WHITESPACE`, `FINALNEWLINE`, `CHARSET`,
  `ENDOFLINE`) and file path, never by message text (it is localised).
- A failing build or format check blocks the commit. Nothing is committed until
  all three commands succeed.

## 9. Commits during implementation

- Format: exactly `skills/issue-to-pr/rules/repo-conventions.md` §2 - a type prefix (`feat:`, `test:`,
  `fix:`), imperative, lowercase after the colon, subject <= 72 characters, issue
  number in parentheses, e.g. `feat: add optimistic concurrency to todo updates (#42)`.
- `docs:` is not used by the implementer (it writes no documentation files).
- Commit in small logical steps: production code (`feat:` / `fix:`) and tests
  (`test:`) in separate commits; never mix them.
- Stage explicit paths under `src/` and `tests/` only (`git add src/... tests/...`).
  `git add .`, `git add -A` and `git commit -a` are forbidden.
- Never create or switch branches, never push, never run `gh`. The branch has
  already been created by Stage 1 and publishing is Stage 4's job.
