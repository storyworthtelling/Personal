using Folio.Domain.Analysis;
using Folio.Domain.Text;

namespace Folio.Domain.Reading;

/// <summary>One reader's score on a draft, beside its score on the draft before.</summary>
public sealed record ReaderScore(string PersonaId, string Name, string Question, int Score, string Reason, int? Previous);

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
                    persona.Id, persona.Name, persona.QuestionFor(length),
                    now[persona.Id].Score, now[persona.Id].ScoreReason,
                    before.TryGetValue(persona.Id, out var score) ? score : null)),
        ];
    }
}
