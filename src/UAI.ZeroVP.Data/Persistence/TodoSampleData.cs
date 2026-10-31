using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Data.InMemory;

/// <summary>Ukázkové úkoly, kterými se úložiště naplní při prvním startu.</summary>
internal static class TodoSampleData
{
    /// <summary>Vytvoří ukázkové úkoly vztažené k zadanému okamžiku.</summary>
    internal static IReadOnlyList<TodoItem> Create(DateTimeOffset now) =>
    [
        Item("Připravit ukázkové API", "Tři projekty: DTO, Data, API.", TodoPriority.High, isCompleted: true, now.AddDays(-2), "anna"),
        Item("Napsat Dockerfile", "Vícefázový build nad SDK a runtime image.", TodoPriority.High, isCompleted: false, now.AddDays(-1), "anna"),
        Item("Vyzkoušet docker compose", "Spustit API ve dvou replikách.", TodoPriority.Normal, isCompleted: false, now.AddHours(-3), "petr"),
        Item("Uklidit nepoužité image", "docker image prune", TodoPriority.Low, isCompleted: false, now.AddMinutes(-30), "petr")
    ];

    private static TodoItem Item(string title, string description, TodoPriority priority, bool isCompleted, DateTimeOffset createdAtUtc, string? userId) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            Priority = priority,
            IsCompleted = isCompleted,
            CreatedAtUtc = createdAtUtc,
            CompletedAtUtc = isCompleted ? createdAtUtc.AddHours(1) : null,
            UpdatedAtUtc = createdAtUtc,
            UserId = userId
        };
}