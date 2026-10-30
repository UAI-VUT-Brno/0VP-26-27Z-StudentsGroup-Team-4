using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using UAI.ZeroVP.API.Logging;
using UAI.ZeroVP.API.Mapping;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.API.Controllers;

/// <summary>
/// REST rozhraní nad seznamem úkolů.
/// Kontroler zná pouze rozhraní <see cref="ITodoRepository"/> – nikoli to,
/// že za ním stojí paměťové úložiště.
/// </summary>
[ApiController]
[Route("api/todos")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public sealed class TodosController : ControllerBase
{
    private readonly ITodoRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TodosController> _logger;

    /// <summary>Vytvoří kontroler se závislostmi z DI kontejneru.</summary>
    public TodosController(ITodoRepository repository, TimeProvider timeProvider, ILogger<TodosController> logger)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Vrátí seznam úkolů, volitelně filtrovaný podle stavu dokončení a uživatele.</summary>
    /// <param name="isCompleted">Filtr <c>true</c>/<c>false</c>; vynechání znamená bez filtru.</param>
    /// <param name="userId">Identifikátor uživatele; vynechání znamená bez filtru.</param>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <response code="200">Seznam úkolů seřazený podle priority a data vzniku.</response>
    /// <response code="400">Hodnota parametru <c>isCompleted</c> není <c>true</c> ani <c>false</c>.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TodoItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<TodoItemDto>>> GetAll(
        [FromQuery] bool? isCompleted,
        [FromQuery] string? userId,
        CancellationToken cancellationToken)
    {
        // ASP.NET Core binds an empty "?userId=" to null, which would silently mean "no filter"
        // and return the tasks of every user. An explicitly present but empty userId matches nothing.
        var userIdFilter = userId is null && Request?.Query.ContainsKey("userId") == true
            ? string.Empty
            : userId;

        var items = await _repository.GetAllAsync(isCompleted, userIdFilter, cancellationToken);

        return Ok(items.Select(item => item.ToDto()).ToList());
    }

    /// <summary>Vrátí jeden úkol podle identifikátoru.</summary>
    /// <response code="200">Nalezený úkol.</response>
    /// <response code="404">Úkol se zadaným identifikátorem neexistuje.</response>
    [HttpGet("{id:guid}", Name = nameof(GetById))]
    [ProducesResponseType(typeof(TodoItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TodoItemDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await _repository.GetByIdAsync(id, cancellationToken);

        return item is null ? NotFoundTodo(id) : Ok(item.ToDto());
    }

    /// <summary>Založí nový úkol.</summary>
    /// <response code="201">Úkol byl vytvořen; hlavička <c>Location</c> odkazuje na nový zdroj.</response>
    /// <response code="400">Vstupní data neprošla validací.</response>
    [HttpPost]
    [ProducesResponseType(typeof(TodoItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TodoItemDto>> Create(
        [FromBody] CreateTodoItemRequest request,
        CancellationToken cancellationToken)
    {
        var entity = request.ToEntity(_timeProvider.GetUtcNow());
        var created = await _repository.AddAsync(entity, cancellationToken);

        _logger.LogInformation(LogEvents.TodoCreated, "Vytvořen úkol {TodoId} ({Title}).", created.Id, created.Title);

        return CreatedAtRoute(nameof(GetById), new { id = created.Id }, created.ToDto());
    }

    /// <summary>
    /// Creates a weekly sequence of the same task: the first occurrence on <c>firstDueAtUtc</c>
    /// plus <c>repeatCount</c> more, each exactly seven days after the previous one.
    /// </summary>
    /// <remarks>
    /// The occurrences are ordinary, independent tasks - no recurrence rule is stored.
    /// Updating, completing or deleting one occurrence does not change the others.
    /// All occurrences are created together or not at all. Like all data, they are kept
    /// in memory only and do not survive an application restart.
    /// </remarks>
    /// <response code="201">
    /// The created occurrences in chronological order (<c>repeatCount</c> + 1 tasks).
    /// There is no <c>Location</c> header because the result is a set of tasks, not one resource.
    /// </response>
    /// <response code="400">
    /// The task fields, the first due date or the repeat count (1-52) did not pass validation.
    /// No task is created.
    /// </response>
    [HttpPost("repeat")]
    [ProducesResponseType(typeof(IReadOnlyList<TodoItemDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<TodoItemDto>>> CreateWeekly(
        [FromBody] CreateWeeklyTodoItemsRequest request,
        CancellationToken cancellationToken)
    {
        var entities = request.ToEntities(_timeProvider.GetUtcNow());
        var created = await _repository.AddRangeAsync(entities, cancellationToken);

        _logger.LogInformation(
            LogEvents.WeeklyTodosCreated,
            "Created {Count} weekly occurrences of task '{Title}' (first due {FirstDueAtUtc}, last due {LastDueAtUtc}).",
            created.Count,
            created[0].Title,
            FormatUtc(created[0].DueAtUtc),
            FormatUtc(created[^1].DueAtUtc));

        // StatusCode instead of CreatedAtRoute: there is no single resource to point a Location header at.
        return StatusCode(StatusCodes.Status201Created, created.Select(item => item.ToDto()).ToList());
    }

    /// <summary>Přepíše existující úkol novými hodnotami.</summary>
    /// <response code="200">Aktualizovaný úkol.</response>
    /// <response code="400">Vstupní data neprošla validací.</response>
    /// <response code="404">Úkol se zadaným identifikátorem neexistuje.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TodoItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TodoItemDto>> Update(
        Guid id,
        [FromBody] UpdateTodoItemRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFoundTodo(id);
        }

        request.ApplyTo(entity, _timeProvider.GetUtcNow());

        return await _repository.UpdateAsync(entity, cancellationToken)
            ? Ok(entity.ToDto())
            : NotFoundTodo(id);
    }

    /// <summary>Označí úkol za dokončený.</summary>
    /// <response code="200">Úkol po změně stavu.</response>
    /// <response code="404">Úkol se zadaným identifikátorem neexistuje.</response>
    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType(typeof(TodoItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TodoItemDto>> Complete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFoundTodo(id);
        }

        entity.SetCompletionState(isCompleted: true, _timeProvider.GetUtcNow());

        return await _repository.UpdateAsync(entity, cancellationToken)
            ? Ok(entity.ToDto())
            : NotFoundTodo(id);
    }

    /// <summary>Smaže úkol.</summary>
    /// <response code="204">Úkol byl smazán.</response>
    /// <response code="404">Úkol se zadaným identifikátorem neexistuje.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!await _repository.DeleteAsync(id, cancellationToken))
        {
            return NotFoundTodo(id);
        }

        _logger.LogInformation(LogEvents.TodoDeleted, "Smazán úkol {TodoId}.", id);

        return NoContent();
    }

    /// <summary>Formats a UTC instant for the log in a culture-independent ISO 8601 form.</summary>
    private static string? FormatUtc(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Jednotná odpověď 404 ve formátu ProblemDetails.</summary>
    private ObjectResult NotFoundTodo(Guid id) => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Úkol nenalezen",
        detail: $"Úkol s identifikátorem {id} neexistuje.");
}
