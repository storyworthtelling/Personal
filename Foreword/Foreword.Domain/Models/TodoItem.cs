namespace Foreword.Domain.Models;

/// <summary>
/// One task as Google holds it.
///
/// <para>Both ids are carried, because the Tasks API addresses a task as
/// <c>lists/{tasklist}/tasks/{task}</c> — so a once task edited in Foreword (D37) has nowhere to be
/// written back to without them, and the two copies drift apart. That is the exact thing the mirror
/// exists to prevent.</para>
/// </summary>
public class TodoItem
{
    /// <summary>Google's own id for the task.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The list it lives in. Half of the address; a task id alone will not find it.</summary>
    public string ListId { get; init; } = string.Empty;

    public required string Title { get; init; }
    public DateOnly? Due { get; init; }

    /// <summary>
    /// Closed in Google's own app. Carried rather than filtered out, so the list can show it struck
    /// through and say why it will not print.
    /// </summary>
    public bool Completed { get; init; }
}
