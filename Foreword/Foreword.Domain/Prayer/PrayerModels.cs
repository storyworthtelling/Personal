namespace Foreword.Domain.Prayer;

/// <summary>
/// One thing being held, as it is stored.
///
/// <para>The text is a <see cref="PrayerText"/> rather than a string, and that is deliberate even
/// though these lines are short: the requests are the half with people's names in them. A record
/// whose text could be handed to anything is a record that will be.</para>
/// </summary>
public sealed record PrayerRequest
{
    public required string Id { get; init; }

    public required PrayerText Text { get; init; }

    /// <summary>The morning it was first held. What <see cref="HoldDuration"/> counts from.</summary>
    public required DateOnly Held { get; init; }

    /// <summary>
    /// The morning it was answered, which is also the morning the strike-through is earned.
    /// </summary>
    public DateOnly? Answered { get; init; }

    /// <summary>
    /// Whether the strike-through has already been printed. It prints <b>once</b>, so the answer is
    /// acknowledged on paper, and then the item stops printing — which needs the page to remember
    /// that it showed it.
    /// </summary>
    public bool StrikePrinted { get; init; }
}

/// <summary>One line the owner wrote in the journal, kept by the date they wrote it.</summary>
public sealed record JournalEntry
{
    public required DateOnly Date { get; init; }

    public required PrayerText Text { get; init; }
}

/// <summary>One row of <c>Holding</c>, ready to draw.</summary>
public readonly record struct PrintedRequest(PrayerText Text, string Since, bool Struck);

/// <summary>One row of <c>From before</c>: what it was, and how long ago in words.</summary>
public readonly record struct SurfacedEntry(string When, PrayerText Text);

/// <summary>
/// The whole two-page prayer document, composed.
///
/// <para><see cref="Lead"/> is the pastoral line and stays a <see cref="PrayerText"/> even though
/// it is Claude's sentence rather than the owner's: it was written <i>from</i> the journal, so
/// handing it to the brief's opening would hand the brief a summary of the journal.</para>
/// </summary>
public sealed record PrayerDocument
{
    public required DateOnly Date { get; init; }

    public PrayerText Lead { get; init; } = PrayerText.Empty;

    public IReadOnlyList<PrintedRequest> Holding { get; init; } = [];

    /// <summary>
    /// This morning's member of each standing rotation. Its own section rather than more rows of
    /// <see cref="Holding"/>: a name that was simply next has no length of time attached to it, and
    /// printing "held 6 weeks" beside one would be the page making something up.
    /// </summary>
    public IReadOnlyList<RotationPicked> Rotations { get; init; } = [];

    public IReadOnlyList<SurfacedEntry> FromBefore { get; init; } = [];
}
