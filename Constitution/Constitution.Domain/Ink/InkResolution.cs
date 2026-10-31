namespace Constitution.Domain.Ink;

/// <summary>
/// What one target (or one circle-a-word group) means once the ink is read. A closed list: every
/// default is a named type here, so silence can only ever mean what the book said it means.
/// <see cref="SubjectId"/> is the target id, or the group id for a circle-a-word choice.
/// </summary>
public abstract record InkResolution(string SubjectId)
{
    /// <summary>Any mark at all, a scribble included (D18).</summary>
    public sealed record Ticked(string SubjectId) : InkResolution(SubjectId);

    /// <summary>An empty workout box, or a "number" box left empty.</summary>
    public sealed record Skipped(string SubjectId) : InkResolution(SubjectId);

    /// <summary>A written reason (D17), which wins over a tick on the same row. Null when the owner chose "leave it out" without words.</summary>
    public sealed record Denied(string SubjectId, string? Reason) : InkResolution(SubjectId);

    /// <summary>An untouched meal-sheet item whose deadline has come.</summary>
    public sealed record ApprovedAtDeadline(string SubjectId) : InkResolution(SubjectId);

    /// <summary>An uncircled rating.</summary>
    public sealed record Unchanged(string SubjectId) : InkResolution(SubjectId);

    /// <summary>"Still sore?" left blank (D28).</summary>
    public sealed record Better(string SubjectId) : InkResolution(SubjectId);

    /// <summary>"Still sore?" marked.</summary>
    public sealed record StillSore(string SubjectId) : InkResolution(SubjectId);

    /// <summary>A staple's line left blank.</summary>
    public sealed record NoNeed(string SubjectId) : InkResolution(SubjectId);

    /// <summary>A staple's line with a mark or words; <paramref name="Text"/> is what was written, if anything legible.</summary>
    public sealed record Needed(string SubjectId, string? Text) : InkResolution(SubjectId);

    /// <summary>A circle-a-word group with exactly one lane circled.</summary>
    public sealed record Circled(string SubjectId, string Word) : InkResolution(SubjectId);

    /// <summary>A written number.</summary>
    public sealed record Written(string SubjectId, string Text) : InkResolution(SubjectId);

    /// <summary>
    /// A circled word with no Done tick on an exercise page (p.5): the page is done and the circled
    /// word is its rating. Carried on the Done box's id; the rating group gets no second entry.
    /// </summary>
    public sealed record PageDone(string SubjectId, string Rating) : InkResolution(SubjectId);

    /// <summary>
    /// Not guessed. Two chips go to the Waiting page as a Caution notice (<see cref="InkAsks"/>).
    /// <paramref name="CauseKey"/> is the notice's cause.
    /// </summary>
    public sealed record NeedsAsking(string SubjectId, string Subject, string First, string Second, string CauseKey)
        : InkResolution(SubjectId);

    /// <summary>Silence that is not yet an answer: a meal item before its deadline, or a box waiting on a question beside it.</summary>
    public sealed record Undecided(string SubjectId) : InkResolution(SubjectId);

    /// <summary>The page was not read, or the read left this target out. This is never silence and never a default.</summary>
    public sealed record NotRead(string SubjectId, string Why) : InkResolution(SubjectId);
}
