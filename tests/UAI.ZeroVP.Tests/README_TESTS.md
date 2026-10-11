# Backend unit tests

Location: `tests/UAI.ZeroVP.Tests` (xUnit, .NET 10).

Run all tests from the repository root:

    dotnet test

| File | What it tests | How |
|---|---|---|
| TodoItemTests | `TodoItem.Clone()` | Clone has equal values but is an independent object |
| TodoMapperTests, TodoMapperCompletionTests, TodoMapperApplyToTests | Mapping request to entity, completion state, applying updates | Direct calls of static mapper methods; checks trimming, blank description becomes null, `CompletedAtUtc` rules |
| InMemoryTodoRepositoryTests, ...SeedTests, ...QueryTests, ...WriteTests | In-memory repository | Real repository without seed data; add, get, update, delete, sorting, filtering, copies, duplicate id, null argument |
| DataRegistrationTests | DI registration `AddInMemoryTodoData` | Small `ServiceCollection`; singleton, options, disabled seed |
| RequestValidationTests, RequestValidationBoundaryTests | Validation attributes of request DTOs | `Validator.TryValidateObject`; title 200 characters, description 1000 characters, empty title, invalid priority |
| TodosControllerTests, TodosControllerUpdateTests, InfoControllerTests | API controllers | Controller is called as a normal class (no HTTP server); checks the result type (Ok, Created, NoContent, 404) and its content |

Tests follow the Arrange-Act-Assert pattern. They need no database, network or Docker.
`Program.cs` (application startup) is not covered, because it would need integration tests.

# Performance & Load Testing

The project includes tests (FullApiPerformanceTests.cs) that verify whether the API meets the maximum response time requirement of under 500 ms. These tests use WebApplicationFactory and run in-memory, which means they measure the raw performance of the code without network latency or database delays.

To run these tests and display the structured output with the measured times, use the following command in the terminal:
dotnet test --logger "console;verbosity=detailed" --filter "FullyQualifiedName~FullApiPerformanceTests"