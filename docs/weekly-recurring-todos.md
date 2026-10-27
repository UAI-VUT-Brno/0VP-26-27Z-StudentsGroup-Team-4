# Weekly recurring tasks

`POST /api/todos/repeat` creates a weekly sequence of the same task in one request.

The request describes the task once, gives the due date of the first occurrence and says
how many weekly repetitions follow. The API creates the first occurrence plus the repetitions,
each exactly seven days after the previous one. For example, `repeatCount: 3` creates **four**
tasks: the first one and three more.

## Request

`POST /api/todos/repeat`, `Content-Type: application/json`

```json
{
  "title": "Weekly status report",
  "description": "Send the status report to the team.",
  "priority": "Normal",
  "firstDueAtUtc": "2026-11-02T09:00:00Z",
  "repeatCount": 3
}
```

| Field | Type | Required | Rules |
|---|---|---|---|
| `title` | string | yes | 1-200 characters; leading and trailing spaces are trimmed |
| `description` | string | no | at most 1000 characters; a blank value is stored as `null` |
| `priority` | string | no | `Low`, `Normal` (default) or `High` |
| `userId` | string | no | owner of the tasks, at most 200 characters; trimmed, a blank value is stored as `null` |
| `firstDueAtUtc` | date-time | yes | due date and time of the first occurrence, ISO 8601 in UTC |
| `repeatCount` | integer | yes | number of **additional** weekly repetitions, 1-52 |

`title`, `description`, `priority` and `userId` are copied to every occurrence.

## Response

`201 Created` with a JSON array of the created tasks in chronological order
(`repeatCount` + 1 items). Every task is a regular `TodoItemDto`:

```json
[
  {
    "id": "5b1c9f0e-2d7a-4c1e-9a52-7f3e8d6b4a10",
    "title": "Weekly status report",
    "description": "Send the status report to the team.",
    "priority": "Normal",
    "isCompleted": false,
    "createdAtUtc": "2026-10-23T10:15:00+00:00",
    "completedAtUtc": null,
    "dueAtUtc": "2026-11-02T09:00:00+00:00"
  },
  {
    "id": "0d6f3c88-91ab-4f0e-b0c4-2a7d5e1f9c33",
    "title": "Weekly status report",
    "description": "Send the status report to the team.",
    "priority": "Normal",
    "isCompleted": false,
    "createdAtUtc": "2026-10-23T10:15:00+00:00",
    "completedAtUtc": null,
    "dueAtUtc": "2026-11-09T09:00:00+00:00"
  }
]
```

The example shows two items for brevity; with `repeatCount: 3` the array has four.
The response has no `Location` header, because the result is a set of tasks and not one resource.
Use `GET /api/todos` or `GET /api/todos/{id}` to read the tasks later.

### Errors

Invalid input returns the API's standard `400 Bad Request` validation response
(`ValidationProblemDetails`) and **creates no tasks**. This applies to:

- an invalid task field (missing or empty `title`, too long `title` or `description`, unknown `priority`),
- a missing or unparsable `firstDueAtUtc`,
- a missing, non-integer or out-of-range `repeatCount` (outside 1-52),
- a first due date so close to the end of the supported date range (year 9999) that the last
  occurrence would not fit,
- a malformed or empty request body.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "RepeatCount": [ "Repeat count must be between 1 and 52." ]
  },
  "traceId": "00-4f2c...-01"
}
```

## Due date: the new `dueAtUtc` field

Every task now has an optional `dueAtUtc` in the API response and in the data model.
Tasks created through `POST /api/todos` have no due date (`dueAtUtc: null`), so the
existing endpoint and its clients are unaffected. Updating a task (`PUT`) or completing it
never changes its due date.

## Behaviour worth knowing

- **Dates are calculated in UTC.** Occurrence *n* is due exactly `n x 7 x 24` hours after
  the first one. Daylight saving time and the server time zone have no effect. A first due date
  sent with a non-zero offset (for example `2026-11-02T10:00:00+01:00`) is converted to UTC.
- **Occurrences are separate regular tasks, not a recurrence rule.** Nothing links them together.
  Completing, updating or deleting one occurrence does not change the others.
- **All or nothing.** The occurrences are stored by one atomic repository call
  (`ITodoRepository.AddRangeAsync`); if storing fails, no occurrence remains.
- **Same creation time.** All occurrences share one `createdAtUtc`. In `GET /api/todos`
  they are listed by priority, then creation time, then due date, so a sequence always
  appears in chronological order.
- **In-memory only.** As with all current data, the tasks do not survive an application restart.

## Examples

```bash
curl -X POST http://localhost:5258/api/todos/repeat \
  -H "Content-Type: application/json" \
  -d '{"title":"Weekly status report","priority":"Normal","firstDueAtUtc":"2026-11-02T09:00:00Z","repeatCount":3}'
```

Ready-made requests (valid and invalid) are in `src/UAI.ZeroVP.API/UAI.ZeroVP.API.http`.
The operation is also described in Swagger UI (`/swagger`) with an example value.

## Where the code is

| Layer | File | Role |
|---|---|---|
| DTO | `CreateWeeklyTodoItemsRequest.cs` | request contract and validation rules |
| DTO | `TodoItemDto.cs` | `DueAtUtc` in the response |
| Data | `TodoItem.cs`, `ITodoRepository.cs`, `InMemoryTodoRepository.cs` | `DueAtUtc`, atomic `AddRangeAsync` |
| API | `TodoMapper.cs` (`ToEntities`) | weekly date calculation |
| API | `TodosController.cs` (`CreateWeekly`) | the endpoint |
| API | `LogEvents.cs` | log event `WeeklyTodosCreated` (3002), see [logging.md](logging.md) |
