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
        UserId = entity.UserId
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
        UpdatedAtUtc = nowUtc,
        UserId = string.IsNullOrWhiteSpace(request.UserId) ? null : request.UserId.Trim()
    };

    /// <summary>
    /// Promítne data z požadavku do existující entity.
    /// Hlídá i přechod stavu dokončení, aby <c>CompletedAtUtc</c> odpovídal příznaku.
    /// </summary>
    public static void ApplyTo(this UpdateTodoItemRequest request, TodoItem entity, DateTimeOffset nowUtc)
    {
        entity.Title = request.Title.Trim();
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.Priority = request.Priority;
        // Vynechané (nebo prázdné) userId vlastníka nemění; přepíše se jen poslaná hodnota.
        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            entity.UserId = request.UserId.Trim();
        }
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
