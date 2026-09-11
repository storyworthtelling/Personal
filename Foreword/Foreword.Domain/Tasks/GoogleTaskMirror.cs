using Foreword.Domain.Models;

namespace Foreword.Domain.Tasks;

/// <summary>
/// Google's tasks, as Foreword sees them, and the one list the page draws (D37).
///
/// <para>A once task is a <b>mirror</b> rather than a copy: its title and due date belong to Google
/// and are written back rather than kept here, so the two can never disagree. What Foreword adds is
/// the part Google Tasks cannot express — a rule, a window, a season — and that never travels the
/// other way.</para>
///
/// <para>The merge happens here rather than at the point of drawing, which is what makes T3's "one
/// list, not three" a fact about the data instead of an arrangement on a screen.</para>
/// </summary>
public static class GoogleTaskMirror
{
    public static IReadOnlyList<ForewordTask> From(IReadOnlyList<TodoItem> tasks) =>
    [
        .. tasks.Select(task => new ForewordTask
        {
            // Google's id, because that is what a write-back is addressed to. Without it an edit in
            // Foreword would have nowhere to go and the two copies would drift apart, which is the
            // whole thing the mirror exists to prevent.
            Id = task.Id,
            GoogleTaskId = task.Id,
            Title = task.Title,
            Kind = TaskKind.Once,
            Due = task.Due,

            // Carried rather than dropped: it shows struck through on the list so the owner can see
            // why it will not print. One that simply vanished would read as one Foreword lost.
            CompletedInGoogle = task.Completed,
        }),
    ];

    /// <summary>
    /// Foreword's own rules and Google's tasks, as one list. A Google task the owner has already
    /// turned into a rule is not carried twice — the rule wins, because it is the richer of the two
    /// and the mirror is the copy.
    /// </summary>
    public static IReadOnlyList<ForewordTask> Merge(
        IReadOnlyList<ForewordTask> rules, IReadOnlyList<TodoItem> google)
    {
        var claimed = rules
            .Where(rule => !string.IsNullOrEmpty(rule.GoogleTaskId))
            .Select(rule => rule.GoogleTaskId!)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. rules,
            .. From([.. google.Where(task => !claimed.Contains(task.Id))]),
        ];
    }
}
