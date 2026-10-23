using UAI.ZeroVP.Data.Entities;

namespace UAI.ZeroVP.Data.Abstractions;

/// <summary>
/// Kontrakt úložiště úkolů.
/// Metody jsou asynchronní záměrně: dnes za rozhraním stojí paměť,
/// zítra může stát databáze v jiném kontejneru a API se nemusí měnit.
/// </summary>
public interface ITodoRepository
{
    /// <summary>Vrátí všechny úkoly, volitelně filtrované podle stavu dokončení.</summary>
    /// <param name="isCompleted">Filtr na dokončenost; <c>null</c> = bez filtru.</param>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    Task<IReadOnlyList<TodoItem>> GetAllAsync(bool? isCompleted = null, CancellationToken cancellationToken = default);

    /// <summary>Vrátí úkol podle identifikátoru, nebo <c>null</c> pokud neexistuje.</summary>
    Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Uloží nový úkol a vrátí jeho výslednou podobu včetně přiděleného <c>Id</c>.</summary>
    Task<TodoItem> AddAsync(TodoItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a batch of new tasks as one all-or-nothing operation and returns their stored form
    /// in the same order. If any task cannot be stored (for example because its <c>Id</c> already
    /// exists), none of the batch is stored and the call throws.
    /// </summary>
    /// <param name="items">The tasks to store. Must not contain <c>null</c> items; an empty batch is allowed.</param>
    /// <param name="cancellationToken">Token for cancelling the operation.</param>
    Task<IReadOnlyList<TodoItem>> AddRangeAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default);

    /// <summary>Přepíše existující úkol. Vrací <c>false</c>, pokud záznam neexistuje.</summary>
    Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default);

    /// <summary>Smaže úkol. Vrací <c>false</c>, pokud záznam neexistuje.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Počet úkolů v úložišti – využívá ho diagnostický endpoint.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
