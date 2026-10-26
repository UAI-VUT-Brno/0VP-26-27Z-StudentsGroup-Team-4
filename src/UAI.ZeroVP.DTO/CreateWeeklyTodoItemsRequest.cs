using System.ComponentModel.DataAnnotations;

namespace UAI.ZeroVP.DTO;

/// <summary>
/// Input for creating a weekly sequence of the same task (HTTP POST /api/todos/repeat).
/// <para>
/// The sequence consists of the first occurrence (due at <see cref="FirstDueAtUtc"/>) plus
/// <see cref="RepeatCount"/> additional occurrences, each exactly seven days after the previous one.
/// For example, <c>RepeatCount = 3</c> creates four tasks.
/// </para>
/// The task fields (<see cref="Title"/>, <see cref="Description"/>, <see cref="Priority"/>) follow
/// the same rules as in <see cref="CreateTodoItemRequest"/>. Validation attributes are evaluated
/// automatically thanks to <c>[ApiController]</c>.
/// </summary>
public sealed record CreateWeeklyTodoItemsRequest : IValidatableObject
{
    /// <summary>Smallest allowed number of additional weekly repetitions.</summary>
    public const int MinRepeatCount = 1;

    /// <summary>Largest allowed number of additional weekly repetitions (one year).</summary>
    public const int MaxRepeatCount = 52;

    /// <summary>Distance between two consecutive occurrences, in days.</summary>
    public const int DaysBetweenOccurrences = 7;

    /// <summary>
    /// Short task title, copied to every occurrence. Required, 1-200 characters.
    /// <para>
    /// Intentionally without the <c>required</c> keyword, for the same reason as
    /// <see cref="CreateTodoItemRequest.Title"/>: a missing field must reach the validation
    /// attributes and produce the standard validation response.
    /// </para>
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Task title is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Title must be 1 to 200 characters long.")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional description, copied to every occurrence. At most 1000 characters.</summary>
    [StringLength(1000, ErrorMessage = "Description must be at most 1000 characters long.")]
    public string? Description { get; init; }

    /// <summary>Task priority, copied to every occurrence. The default is <see cref="TodoPriority.Normal"/>.</summary>
    [EnumDataType(typeof(TodoPriority), ErrorMessage = "Invalid priority value.")]
    public TodoPriority Priority { get; init; } = TodoPriority.Normal;

    /// <summary>
    /// Optional identifier of the user who owns the tasks (name, ID or e-mail), copied to every
    /// occurrence. At most 200 characters. A blank value is stored as <c>null</c>.
    /// </summary>
    [StringLength(200, ErrorMessage = "User identifier must be at most 200 characters long.")]
    public string? UserId { get; init; }

    /// <summary>
    /// Date and time of the first occurrence, in UTC. Required.
    /// <para>
    /// Nullable on purpose: a missing value must be distinguishable from a real date and be
    /// reported by <c>[Required]</c> instead of silently becoming <c>0001-01-01</c>.
    /// </para>
    /// </summary>
    [Required(ErrorMessage = "First due date (firstDueAtUtc) is required.")]
    public DateTimeOffset? FirstDueAtUtc { get; init; }

    /// <summary>
    /// Number of additional weekly repetitions after the first occurrence, 1 to 52.
    /// A missing value is read as 0 and rejected by the range check.
    /// </summary>
    [Range(MinRepeatCount, MaxRepeatCount, ErrorMessage = "Repeat count must be between 1 and 52.")]
    public int RepeatCount { get; init; }

    /// <summary>
    /// Cross-field rule: the last occurrence must still be a representable date.
    /// Without it, a first due date close to the end of the supported range would make the
    /// date calculation throw and the client would get 500 instead of 400.
    /// Runs only when the property-level checks above have passed.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FirstDueAtUtc is { } firstDueAtUtc
            && RepeatCount is >= MinRepeatCount and <= MaxRepeatCount)
        {
            var span = TimeSpan.FromDays((double)DaysBetweenOccurrences * RepeatCount);

            if (DateTimeOffset.MaxValue - firstDueAtUtc < span)
            {
                yield return new ValidationResult(
                    "The last occurrence would be later than the latest supported date.",
                    new[] { nameof(FirstDueAtUtc), nameof(RepeatCount) });
            }
        }
    }
}