using Folio.Domain.Analysis;
using Folio.Domain.Text;

namespace Folio.Domain.Reading;

/// <summary>One reader's score on a draft, beside its score on the draft before.</summary>
public sealed record ReaderScore(string PersonaId, string Name, string Question, int Score, string Reason, int? Previous)
{
    /// <summary>The reader's role ("Agent") beside its name ("Hugo Hook", R42).</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>This reader's score on every draft it scored, in draft order, for the phone's trend line.</summary>
    public IReadOnlyList<DraftScore> History { get; init; } = [];
}

/// <summary>One reader's score on one draft.</summary>
public sealed record DraftScore(int Draft, int Score);

/// <summary>
/// The phone's trend line for one reader (R13, R37): drafts spread evenly across the width, a 10 at the
/// top and a 1 at the foot. One line per reader, in ink, beside its own score, so seven readers need
/// neither seven colours nor a legend.
/// </summary>
public static class ScoreTrend
{
    public readonly record struct Point(float X, float Y);

    /// <summary>The line's points, or none when there is only one draft to draw.</summary>
    public static IReadOnlyList<Point> Points(IReadOnlyList<DraftScore> history, float width, float height)
    {
        if (history.Count < 2)
            return [];
        var step = width / (history.Count - 1);
        return [.. history.Select((entry, i) => new Point(i * step, height - (Math.Clamp(entry.Score, 1, 10) - 1) / 9f * height))];
    }
}

/// <summary>
/// The readers' scores as the report and the book page say them — one copy of the words, so the
/// page on the tablet and the screen on the phone cannot phrase the same movement two ways.
/// </summary>
public static class ReaderScores
{
    public static string OutOfTen(int score) => $"{score}/10";

    /// <summary>"7/10 · up from 5 last draft" — the line the report and the book page both print.</summary>
    public static string Line(int score, int? previous) =>
        Movement(score, previous) is { } movement ? $"{OutOfTen(score)} · {movement}" : OutOfTen(score);

    /// <summary>
    /// Each reader's scores across drafts, in draft order; a draft the reader did not score is skipped.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<DraftScore>> History(
        IEnumerable<(int Draft, IEnumerable<Review> Reads)> drafts) =>
        drafts.OrderBy(draft => draft.Draft)
            .SelectMany(draft => draft.Reads
                .Where(review => review.Score is >= 1 and <= 10)
                .Select(review => (review.PersonaId, Entry: new DraftScore(draft.Draft, review.Score))))
            .GroupBy(item => item.PersonaId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<DraftScore>)[.. group.Select(item => item.Entry)]);

    /// <summary>How a score moved since the last draft; null when there is nothing to compare with.</summary>
    public static string? Movement(int score, int? previous) => previous switch
    {
        null or < 1 => null,
        { } before when score > before => $"up from {before} last draft",
        { } before when score < before => $"down from {before} last draft",
        _ => "unchanged from last draft",
    };

    /// <summary>
    /// The scored reads of one draft, in reading order, each paired with the same reader's score on
    /// the draft before. A read with no score (stored before scores existed) is left out.
    /// </summary>
    public static IReadOnlyList<ReaderScore> Compare(
        IEnumerable<Review> current, IEnumerable<Review> previous, StoryLength length = StoryLength.Novel)
    {
        var before = previous
            .Where(review => review.Score is >= 1 and <= 10)
            .GroupBy(review => review.PersonaId)
            .ToDictionary(group => group.Key, group => group.Last().Score);
        var now = current
            .Where(review => review.Score is >= 1 and <= 10)
            .GroupBy(review => review.PersonaId)
            .ToDictionary(group => group.Key, group => group.Last());

        return
        [
            .. PersonaCatalogue.Readers
                .Where(persona => now.ContainsKey(persona.Id))
                .Select(persona => new ReaderScore(
                    persona.Id, persona.ReaderName ?? persona.Name, persona.QuestionFor(length),
                    now[persona.Id].Score, now[persona.Id].ScoreReason,
                    before.TryGetValue(persona.Id, out var score) ? score : null)
                {
                    Role = persona.Name,
                }),
        ];
    }
}
