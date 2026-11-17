---
name: failure-parsing
description: >
  Shared prompt fragment for the QA/Self-Healing agent (Stage 3): how to read
  `dotnet build` / `dotnet test` output, extract compiler diagnostics (CSxxxx and
  other ids) and xUnit assertion failures, and map each of them to the offending
  file. Locale-independent: everything is keyed on diagnostic ids, file paths and
  TRX fields, never on message text.
---

# Failure Parsing Guide

Paths are from the repository root. This file is read by
`skills/issue-to-pr/agents/qa-self-healing.md` before the first test run. It
describes **how to read** failures; what to do about them is decided by the agent.

## 1. Golden rule: never parse message text

The .NET tools print messages in the system language (for example Czech), so words
such as "Failed", "error" or "Passed" in prose are unreliable. Use only:

- the **process exit code** (`0` = success, non-zero = failure);
- **diagnostic ids** (`CS0103`, `CA5351`, `NU1903`, `MSB3027`, ...);
- **file paths and line numbers**;
- **TRX fields** (§3), which are XML and locale-independent.

Message text is copied verbatim into the report, but no decision depends on it.

## 2. Compiler and analyzer diagnostics (`dotnet build`, and the build part of `dotnet test`)

Line shape (MSBuild canonical format):

```text
<path>(<line>,<col>): error <ID>: <message> [<project>.csproj]
```

Pattern: `^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\): (?<sev>error|warning) (?<id>[A-Za-z]+\d+): (?<msg>.*?) \[(?<proj>[^\]]+\.csproj)\]$`

Rules:

1. Only `error` lines matter. Ignore `warning` lines unless the same id also appears as an error.
2. MSBuild prints each diagnostic more than once (once per target/project). Deduplicate on `(file, line, col, id)`.
3. Convert the absolute path to a repository-relative one. A diagnostic whose file is not under `src/` or `tests/` is **out of scope** (the agent must not edit it).
4. Group by id family:

| Id family | Meaning | Fixable by the agent? |
|-----------|---------|:---:|
| `CS####` | C# compiler error | yes, in the file named by the diagnostic |
| `CA####`, `IDE####`, `SYSLIB####` | analyzer/security rule promoted to error | yes, fix the code; never suppress the rule |
| `NU####` | NuGet restore / package audit | no - report as out of scope |
| `MSB####`, `NETSDK####` | build system / SDK | no - report as out of scope |
| anything else | unknown | no - report, do not guess |

5. Cascades: one root error often causes many follow-ups. Fix in this order and
   re-run: missing type/member (`CS0246`, `CS0103`, `CS1061`, `CS0117`) -> signature
   mismatch (`CS1501`, `CS1503`, `CS7036`, `CS0535`) -> syntax (`CS1002`, `CS1026`)
   -> the rest. Syntax errors (`CS1xxx`) first if present, because they hide all others.
6. The offending file is the one in the diagnostic. The *cause* can be elsewhere
   (an interface changed, an implementation was not updated: `CS0535`, `CS7036`).
   When the id says "member of another type is missing or has a different shape",
   open that type too and fix the side that disagrees with the plan.

Example:

```text
/repo/src/UAI.ZeroVP.Data/InMemory/InMemoryTodoRepository.cs(58,31): error CS0535: 'InMemoryTodoRepository' does not implement interface member 'ITodoRepository.UpdateAsync(TodoItem, int, CancellationToken)' [/repo/src/UAI.ZeroVP.Data/UAI.ZeroVP.Data.csproj]
```

-> `id = CS0535`, `file = src/UAI.ZeroVP.Data/InMemory/InMemoryTodoRepository.cs`, `line = 58`.
The interface changed; the implementation (this file) is the one to fix. Also check
the other implementation, `Persistence/Sharedjsonfiletodorepository.cs`, for the same error.

## 3. Test failures: use the TRX file, not the console

The agent runs tests with a TRX logger (see the agent file for the exact command).
Parse the `.trx` XML:

| TRX element / attribute | Use |
|-------------------------|-----|
| `UnitTestResult@testName`, `@outcome` | `outcome="Failed"` marks a failure; `testName` is the display name |
| `UnitTestResult/Output/ErrorInfo/Message` | assertion text (copy it into the report) |
| `UnitTestResult/Output/ErrorInfo/StackTrace` | frames with `in <path>:line <n>` |
| `TestDefinitions/UnitTest/TestMethod@className`, `@name` | fully qualified class and method |
| `ResultSummary/Counters@total,@passed,@failed` | counts for the report |

Console fallback (only if no TRX was produced): the console failure block looks like

```text
  Failed UAI.ZeroVP.Tests.TodoMapperTests.ToDto_MapsVersion [12 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 2
Actual:   1
  Stack Trace:
     at UAI.ZeroVP.Tests.TodoMapperTests.ToDto_MapsVersion() in /repo/tests/UAI.ZeroVP.Tests/TodoMapperTests.cs:line 41
```

Take the dotted name after the first token of the line that is followed by
`[<duration>]`, and the `in <path>:line <n>` frames. Do not rely on the word "Failed".

### 3.1 Mapping a failed test to a file

1. **Test file:** `className` -> the file under `tests/UAI.ZeroVP.Tests/` that declares it. Usually `<ClassName>.cs`;
   confirm with a search for `class <ClassName>` instead of assuming.
2. **Frames:** read the stack trace top to bottom and skip frames whose namespace
   starts with `Xunit.`, `System.`, `Microsoft.`, `Moq.` or whose path is not in the repo.
3. **Production suspect:** the first remaining frame whose path is under `src/` is the
   suspect production file. If no frame is under `src/` (typical for `Assert.*` failures),
   open the test line named by the first `tests/` frame, find the call into the system
   under test on or above that line, and follow it into `src/`.
4. **Test suspect:** if the exception is thrown inside the test arrange step (a
   `NullReferenceException` or `InvalidOperationException` on a `tests/` frame, a wrong
   constructor argument), the test itself is the suspect.

### 3.2 Exception type -> first hypothesis

| Exception in `Message` | First hypothesis |
|------------------------|------------------|
| `Xunit.Sdk.EqualException` / `Assert.Equal() Failure` | Production returns a different value; compare with the requirement it encodes. For HTTP tests `Expected: OK / Actual: BadRequest` (or `NotFound`, `Conflict`) points at validation, routing or `ProblemDetails` mapping in `src/UAI.ZeroVP.API`. |
| `Xunit.Sdk.NotNullException` / `NullReferenceException` in `src/` | Missing mapping or an initialisation missed for a newly added property (for example `Clone()` not copying it). |
| `Xunit.Sdk.ThrowsException` (expected exception not thrown / different type) | Missing validation or wrong exception type in `src/`. |
| `Xunit.Sdk.TrueException` / `FalseException` | Read the asserted expression; usually a missed code path. |
| `System.Net.Http.HttpRequestException`, `SocketException`, "address already in use", timeout | Environment problem (port, process start). Not a code defect: report as out of scope. |
| `TypeLoadException`, `MissingMethodException` | Stale build or an interface changed without all implementations: rebuild once, then treat as compile cascade. |

## 4. Fingerprint

For every failure keep a fingerprint: `<id or test FQN> | <file> | <line>`. The agent compares
fingerprints between runs to see which failures are new, fixed or unchanged (§ "Progress
check" in the agent file).

## 5. Output of this step

A list of failure records, ordered by the fix order of §2.5, then by test class:

```json
{
  "kind": "compile | test | out-of-scope",
  "id": "CS0535 | UAI.ZeroVP.Tests.TodoMapperTests.ToDto_MapsVersion",
  "file": "src/UAI.ZeroVP.Data/InMemory/InMemoryTodoRepository.cs",
  "line": 58,
  "message": "<verbatim, first line>",
  "suspect": "production | test"
}
```
