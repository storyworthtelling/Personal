namespace Constitution.Domain.Ink;

/// <summary>
/// What the book wrote down about one place a pen can answer: which targets it spans and what silence
/// and each mark mean there. The caller builds these from the page it painted; the resolver applies them
/// and invents nothing beyond them. <paramref name="Subject"/> is the owner's words for the thing, used
/// when the answer has to be asked ("Was it ticked or not ticked push-ups?").
/// </summary>
public abstract record InkRule(string Subject)
{
    /// <summary>Plain words for the thing; never blank, because a question about nothing cannot be asked.</summary>
    public string Subject { get; init; } = string.IsNullOrWhiteSpace(Subject)
        ? throw new ArgumentException("A rule needs a few words for what it is about, to ask about it if the read is unsure.", nameof(Subject))
        : Subject.Trim();
}

/// <summary>A large workout box: any mark is a tick, empty is skipped.</summary>
public sealed record WorkoutBoxRule(string Subject, string BoxId) : InkRule(Subject);

/// <summary>An exercise page (p.5): the Done tick and the easy / right / hard circle (group <paramref name="RatingGroup"/>).</summary>
public sealed record ExercisePageRule(string Subject, string DoneBoxId, string RatingGroup) : InkRule(Subject);

/// <summary>A meal-sheet row (p.7): the YES tick and the "No, because" field, approved if untouched by <paramref name="Deadline"/>.</summary>
public sealed record MealItemRule(string Subject, string YesBoxId, string ReasonId, DateTimeOffset Deadline) : InkRule(Subject);

/// <summary>A circle-a-word choice on its own (Loved / Fine / Never again): uncircled is unchanged.</summary>
public sealed record RatingRule(string Subject, string Group) : InkRule(Subject);

/// <summary>"Still sore?" on an injury day (p.5): blank means better.</summary>
public sealed record StillSoreRule(string Subject, string BoxId) : InkRule(Subject);

/// <summary>A staple's line on the grocery page: blank means no need.</summary>
public sealed record StapleRule(string Subject, string LineId) : InkRule(Subject);

/// <summary>A written number in its one open box (assessments).</summary>
public sealed record NumberRule(string Subject, string BoxId) : InkRule(Subject);
