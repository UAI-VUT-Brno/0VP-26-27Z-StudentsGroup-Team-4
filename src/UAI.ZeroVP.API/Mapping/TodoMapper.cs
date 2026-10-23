using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.API.Mapping;

/// <summary>
/// Převody mezi entitou datové vrstvy a přenosovými objekty API.
/// Mapování patří do API vrstvy – ta jediná zná obě strany.
/// </summary>
public static class TodoMapper
{
    /// <summary>Převede entitu na výstupní DTO (interní pole se zahodí).</summary>
    public static TodoItemDto ToDto(this TodoItem entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Description = entity.Description,
        Priority = entity.Priority,
        IsCompleted = entity.IsCompleted,
        CreatedAtUtc = entity.CreatedAtUtc,
        CompletedAtUtc = entity.CompletedAtUtc,
        DueAtUtc = entity.DueAtUtc
    };

    /// <summary>Vytvoří novou entitu z požadavku na založení úkolu.</summary>
    /// <param name="request">Data z těla požadavku.</param>
    /// <param name="nowUtc">Aktuální čas – předává se zvenčí kvůli testovatelnosti.</param>
    public static TodoItem ToEntity(this CreateTodoItemRequest request, DateTimeOffset nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        Title = request.Title.Trim(),
        Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
        Priority = request.Priority,
        IsCompleted = false,
        CreatedAtUtc = nowUtc,
        CompletedAtUtc = null,
        UpdatedAtUtc = nowUtc
    };

    /// <summary>
    /// Creates the entities of a weekly sequence: the first occurrence plus
    /// <see cref="CreateWeeklyTodoItemsRequest.RepeatCount"/> more, each exactly seven days
    /// after the previous one. The result is in chronological order.
    /// <para>
    /// Every occurrence is a separate, regular task with its own <c>Id</c> and due date.
    /// Title, description and priority are copied (title and description trimmed as for a single
    /// task) and every occurrence starts incomplete. All occurrences share the same creation time.
    /// </para>
    /// <para>
    /// The first due date is normalized to UTC and the calculation is plain UTC arithmetic
    /// (adds 7 x 24 hours), so it is not affected by daylight saving time or the server time zone.
    /// </para>
    /// </summary>
    /// <param name="request">Validated request; <c>FirstDueAtUtc</c> must be set.</param>
    /// <param name="nowUtc">Current time - passed in from outside for testability.</param>
    public static IReadOnlyList<TodoItem> ToEntities(this CreateWeeklyTodoItemsRequest request, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        var firstDueAtUtc = request.FirstDueAtUtc?.ToUniversalTime()
            ?? throw new ArgumentException("FirstDueAtUtc must be set.", nameof(request));

        var title = request.Title.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        var occurrences = new List<TodoItem>(request.RepeatCount + 1);
        for (var index = 0; index <= request.RepeatCount; index++)
        {
            occurrences.Add(new TodoItem
            {
                Id = Guid.NewGuid(),
                Title = title,
                Description = description,
                Priority = request.Priority,
                IsCompleted = false,
                CreatedAtUtc = nowUtc,
                CompletedAtUtc = null,
                DueAtUtc = firstDueAtUtc.AddDays(CreateWeeklyTodoItemsRequest.DaysBetweenOccurrences * index),
                UpdatedAtUtc = nowUtc
            });
        }

        return occurrences;
    }

    /// <summary>
    /// Promítne data z požadavku do existující entity.
    /// Hlídá i přechod stavu dokončení, aby <c>CompletedAtUtc</c> odpovídal příznaku.
    /// <c>DueAtUtc</c> se záměrně nemění: aktualizační požadavek ho neobsahuje,
    /// takže úprava úkolu termín nikdy nesmaže.
    /// </summary>
    public static void ApplyTo(this UpdateTodoItemRequest request, TodoItem entity, DateTimeOffset nowUtc)
    {
        entity.Title = request.Title.Trim();
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.Priority = request.Priority;
        entity.SetCompletionState(request.IsCompleted, nowUtc);
        entity.UpdatedAtUtc = nowUtc;
    }

    /// <summary>Nastaví příznak dokončení a dopočítá čas dokončení.</summary>
    public static void SetCompletionState(this TodoItem entity, bool isCompleted, DateTimeOffset nowUtc)
    {
        if (isCompleted && !entity.IsCompleted)
        {
            entity.CompletedAtUtc = nowUtc;
        }
        else if (!isCompleted)
        {
            entity.CompletedAtUtc = null;
        }

        entity.IsCompleted = isCompleted;
        entity.UpdatedAtUtc = nowUtc;
    }
}
