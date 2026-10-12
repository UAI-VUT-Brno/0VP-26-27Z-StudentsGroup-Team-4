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

The project includes tests (`FullApiPerformanceTests.cs`) that verify whether every API endpoint meets the maximum response time requirement of 500 ms. These tests use `WebApplicationFactory` and run in-memory, which means they measure the performance of the application code without network latency or real database delays. The results therefore do not cover the full deployment (e.g. a Docker container behind a reverse proxy).

## What is tested

All endpoints are covered:

| Method | Endpoint |
|---|---|
| GET | `/api/info` |
| GET | `/api/todos` |
| GET | `/api/todos/{id}` |
| POST | `/api/todos` |
| PUT | `/api/todos/{id}` |
| POST | `/api/todos/{id}/complete` |
| DELETE | `/api/todos/{id}` |

Each endpoint is tested in two ways:

- **Single user:** one request after a warm-up request.
- **Multi-user:** 2500 concurrent requests. The assertion is made on the slowest request.

## How the tests work

- Only the request itself is measured. Preparing test data and cleanup are not included in the measured time.
- Endpoints with `{id}` get a freshly created todo for every simulated user, so concurrent PUT, complete or DELETE requests do not collide on the same item.
- After each test, the todos created by the test are deleted, so the tests do not affect each other (the application instance is shared within the test class).
- The application runs with its normal logging configuration, including the console logger.

## Running the tests

Run from the repository root (the folder with `UAI.ZeroVP.sln`):

    dotnet test --logger "console;verbosity=detailed" --filter "FullyQualifiedName~FullApiPerformanceTests"

To save the results to a file, use the TRX logger:

    dotnet test --logger "console;verbosity=detailed" --logger "trx;LogFileName=results.trx" --filter "FullyQualifiedName~FullApiPerformanceTests"

The file is saved to `tests/UAI.ZeroVP.Tests/TestResults/`.

Note: do not redirect the console output (`> file.txt`) when running the load tests. The application writes a log line for every request to the console, and slow console output can delay responses and make the tests fail on the 500 ms limit.