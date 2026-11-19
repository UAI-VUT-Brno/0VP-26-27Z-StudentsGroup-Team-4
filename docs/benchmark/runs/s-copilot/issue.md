**Title:** 0VP-T4-S1 Add overdue todos query endpoint

Description:
Clients currently have to download all todos and filter by `DueAtUtc` on their
side. Add a read-only query that returns the todos that are overdue.

Requirements:
- Add `GET /api/todos/overdue` returning `200 OK` with a list of `TodoItemDto`.
- A todo is overdue when it is not completed, has a `DueAtUtc`, and `DueAtUtc`
  is strictly earlier than the reference time. A todo that is due exactly at
  the reference time is not overdue.
- The reference time is the current UTC time by default. An optional query
  parameter `asOfUtc` (ISO 8601 date and time with offset) overrides it so the
  endpoint can be tested deterministically. An unparsable value returns
  `400 Bad Request` as `ProblemDetails`.
- An optional query parameter `userId` restricts the result to the todos of
  that user, the same way `GET /api/todos?userId=` does.
- Results are ordered by `DueAtUtc` ascending, then by `Priority` descending.
- Add the query to the repository contract as a new method on
  `ITodoRepository`; every existing implementation must implement it. The
  controller must not filter in memory.
- Completed todos and todos without a due date are never returned.
- The route `overdue` must not conflict with the existing `{id:guid}` route.

Acceptance Criteria:
- `GET /api/todos/overdue` returns only incomplete todos with a due date
  strictly before the reference time.
- A todo due exactly at `asOfUtc` is not returned; one due a second earlier is.
- `userId` filtering works and combines with the overdue rule.
- Ordering is by `DueAtUtc` ascending, then `Priority` descending.
- An invalid `asOfUtc` returns `400` with a `ProblemDetails` body.
- All `ITodoRepository` implementations compile and are covered by unit tests;
  the endpoint is covered by an API-level test.
- `dotnet format --verify-no-changes` and the CI test command pass.
- The public change is described in the README endpoint documentation.
