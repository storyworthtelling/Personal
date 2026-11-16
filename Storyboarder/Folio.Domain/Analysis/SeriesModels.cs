namespace Folio.Domain.Analysis;

// Series awareness (Phase 5). When a book is book N of a series, the prior book's finished canon is reused so
// this book's report can check cross-book continuity. These types carry that canon IN to the continuity read
// and its verdict OUT — folded into the report like the craft/voice/expansion halves.

// One entity in the prior book's canon: its name and the description the prior book established for it. The
// continuity read compares this against how the same entity appears in the current book.
public sealed record CanonEntity(string Name, string Description);

// The prior book's established canon, assembled from its finished review and handed to the continuity read:
// its title, its design-bible cast and settings, where the story stood at its end (its continuity summary),
// and the finding threads it left unresolved (its open problems). Empty when the prior book has no canon yet.
public sealed record SeriesCanon(
    string PriorBookTitle,
    IReadOnlyList<CanonEntity> Cast,
    IReadOnlyList<CanonEntity> Settings,
    string PriorContinuitySummary,
    IReadOnlyList<ReportFinding> OpenThreads)
{
    public static SeriesCanon Empty { get; } = new(
        string.Empty, Array.Empty<CanonEntity>(), Array.Empty<CanonEntity>(),
        string.Empty, Array.Empty<ReportFinding>());
}

// One place THIS book contradicts the prior book's established canon: cited to a chapter of this book, what the
// prior book established, and what this book says instead.
public sealed record SeriesBreak(string Chapter, string Established, string Contradiction)
{
    // A verbatim line from THIS book where the clash is, so the contradiction is written on that line
    // rather than listed. Empty when the model gave only a chapter.
    public string Excerpt { get; init; } = string.Empty;

    /// <summary>
    /// How much this matters — "Must", "Should" or "Consider" — as the model judged it when it wrote
    /// the finding (R8). Empty on a report stored before findings carried one; the mark then takes the
    /// revision plan's tier for its chapter.
    /// </summary>
    public string Severity { get; init; } = string.Empty;

    /// <summary>
    /// Which of the four things the continuity read checks (R22), one of <see cref="ContinuityKinds"/>.
    /// Empty on a report stored before the series bible: it reads as a contradicted fact.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>The earlier book this contradicts (R23, R25), or empty when the read did not say.</summary>
    public string Book { get; init; } = string.Empty;

    /// <summary>The earlier book's own words for what it established, from the series bible (R24), or empty.</summary>
    public string Quote { get; init; } = string.Empty;
}

/// <summary>The four things the continuity read checks against the series bible (R22).</summary>
public static class ContinuityKinds
{
    public const string ContradictedFact = "Contradicted fact";
    public const string CharacterDrift = "Character drift";
    public const string DroppedThread = "Dropped thread";
    public const string TimelineError = "Timeline error";

    public static IReadOnlyList<string> All { get; } = [ContradictedFact, CharacterDrift, DroppedThread, TimelineError];

    /// <summary>One of the four; anything else is a contradicted fact, the plainest of them.</summary>
    public static string Normalize(string? kind) =>
        All.FirstOrDefault(known => string.Equals(known, kind?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? ContradictedFact;
}

// A prior-book open thread carried forward, and how this book handles it — "Addressed", "Ignored", or
// "Contradicted" — so a problem spanning books is tracked rather than silently forgotten.
public sealed record CarriedThread(string Established, string Status);

// The cross-book continuity read: the prior book it was checked against, the contradictions of that book's
// canon, and the status of its carried-over open threads. Its own resumable Claude call and DB column, folded
// into the report at render. Empty (and unrendered) for a standalone book — quiet by default.
public sealed record SeriesContinuity(
    string PriorBookTitle,
    IReadOnlyList<SeriesBreak> Breaks,
    IReadOnlyList<CarriedThread> CarriedThreads)
{
    public static SeriesContinuity Empty { get; } =
        new(string.Empty, Array.Empty<SeriesBreak>(), Array.Empty<CarriedThread>());

    // True when there is anything worth a section — the renderer stays silent otherwise (a standalone book or a
    // clean sequel shows nothing).
    public bool HasContent => Breaks.Count > 0 || CarriedThreads.Count > 0;

    /// <summary>
    /// While the book's place in the series is Folio's guess (R21), the sentence saying what the read
    /// assumed; empty once the owner has confirmed it.
    /// </summary>
    public string Assumption { get; init; } = string.Empty;
}
