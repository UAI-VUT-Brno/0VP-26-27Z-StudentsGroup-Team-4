using UAI.ZeroVP.Data.Entities;

namespace UAI.ZeroVP.Data.Abstractions;

/// <summary>
/// Kontrakt úložiště úkolů.
/// </summary>
public interface ITodoRepository
{
    /// <summary>Vrátí všechny úkoly, volitelně filtrované podle stavu dokončení a uživatele.</summary>
    Task<IReadOnlyList<TodoItem>> GetAllAsync(bool? isCompleted = null, string? userId = null, CancellationToken cancellationToken = default);

    /// <summary>Vrátí úkol podle identifikátoru, nebo <c>null</c> pokud neexistuje.</summary>
    Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Uloží nový úkol a vrátí jeho výslednou podobu včetně přiděleného <c>Id</c>.</summary>
    Task<TodoItem> AddAsync(TodoItem item, CancellationToken cancellationToken = default);

    /// <summary>Uloží dávku úkolů jako atomickou operaci.</summary>
    Task<IReadOnlyList<TodoItem>> AddRangeAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default);

    /// <summary>Přepíše existující úkol. Vrací <c>false</c>, pokud záznam neexistuje.</summary>
    Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default);

    /// <summary>Smaže úkol. Vrací <c>false</c>, pokud záznam neexistuje.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Počet úkolů v úložišti.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Načte úkoly z persistentního JSON souboru.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Uloží úkoly do persistentního JSON souboru.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}