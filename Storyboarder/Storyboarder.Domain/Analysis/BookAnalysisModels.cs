namespace Storyboarder.Domain.Analysis;

// The per-chunk literary observations (the "map" output for the revision report). Everything is a
// flat list of short strings so one chunk's notes stay compact enough to fold many together in the
// reduce step. The client maps missing fields to empty lists, so a partial reply degrades gracefully.
//
// Chapter is the label of the chapter(s) the chunk covers ("Chapter 7", "Chapters 7–8"), stamped on
// by the client rather than asked of the model: it is known exactly from the chunk's offsets, and it
// is what lets every finding in the consolidated report cite a place in the book. Empty for a
// conversion chunked before chapter detection existed.
public sealed record ChapterNotes(
    string Chapter,
    IReadOnlyList<string> CharactersPresent,
    IReadOnlyList<string> KeyEvents,
    IReadOnlyList<string> CharacterBeats,
    IReadOnlyList<string> SetupsAndPayoffs,
    IReadOnlyList<string> PossibleInconsistencies,
    IReadOnlyList<string> FactualClaims,
    IReadOnlyList<string> NotableStrengths,
    IReadOnlyList<string> NotableWeaknesses)
{
    public static ChapterNotes Empty { get; } = new(
        string.Empty,
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
}

// A character's place in the story: role, arc, and key relationships (rendered as the character map).
public sealed record CharacterProfile(
    string Name,
    string Role,
    string ArcSummary,
    IReadOnlyList<string> Relationships);

// One review from a specific, fair perspective (e.g. developmental editor, target-genre reader,
// literary critic, acquiring agent) with balanced strengths and weaknesses.
public sealed record Review(
    string Perspective,
    string Assessment,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    string Recommendation);

// One actionable revision suggestion. The report lists these highest-impact-first.
public sealed record RevisionItem(
    string Priority,
    string Issue,
    string SuggestedFix);

// The whole consolidated revision report (the "reduce" output), rendered into the analysis PDF and
// aimed at helping the author revise toward publication.
public sealed record BookAnalysisResult(
    string Logline,
    string Synopsis,
    IReadOnlyList<CharacterProfile> Characters,
    IReadOnlyList<string> Themes,
    IReadOnlyList<string> PlotHoles,
    IReadOnlyList<string> Inconsistencies,
    IReadOnlyList<string> Inaccuracies,
    IReadOnlyList<string> PacingNotes,
    IReadOnlyList<Review> Reviews,
    IReadOnlyList<RevisionItem> RevisionPlan,
    IReadOnlyList<string> ComparableTitles,
    string MarketPositioning)
{
    public static BookAnalysisResult Empty { get; } = new(
        string.Empty, string.Empty, Array.Empty<CharacterProfile>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<Review>(), Array.Empty<RevisionItem>(), Array.Empty<string>(), string.Empty);
}
