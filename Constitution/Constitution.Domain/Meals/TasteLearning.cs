using Constitution.Domain.Household;
using Constitution.Domain.Recipes;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Meals;

/// <summary>Where a piece of written evidence came from.</summary>
public enum TasteEvidenceKind
{
    /// <summary>The "No, because" field on the meal sheet (D32).</summary>
    Denial,

    /// <summary>The cook's recipe-margin note (D35).</summary>
    MarginNote,

    /// <summary>A reason written beside a rating.</summary>
    RatingReason,
}

/// <summary>
/// Words someone wrote about food, who wrote them, the dish they were about (when there was one) and the day. This is
/// all a judge is ever shown: no profile, no other person's taste, nothing about Body.
/// </summary>
public sealed record TasteEvidence(PersonId Person, TasteEvidenceKind Kind, string Words, RecipeId? Recipe, DateOnly Day)
{
    public const int MaxWordsLength = 1000;

    /// <summary>Throws <see cref="ArgumentException"/> naming what is wrong.</summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Words) || Words.Length > MaxWordsLength)
        {
            throw new ArgumentException("Evidence is 1 to " + MaxWordsLength + " characters of words.", nameof(Words));
        }
    }
}

/// <summary>One thing the judge says is true of a person's taste for good.</summary>
public sealed record TasteLesson(TasteSubjectKind Kind, string Subject, TasteStance Stance);

/// <summary>What the judge decided about one piece of evidence.</summary>
public abstract record TasteJudgement
{
    /// <summary>A lasting lesson (or a few).</summary>
    public sealed record Lasting(IReadOnlyList<TasteLesson> Lessons) : TasteJudgement;

    /// <summary>Said once, about tonight; it teaches nothing.</summary>
    public sealed record OneOff : TasteJudgement;

    /// <summary>
    /// No judge answered. The reason reuses the workouts' closed list; the detail is a specific sentence of what happened (a status, a type name; never the request, the words
    /// or a key), and the cause keeps the failure's own stack when there was one.
    /// </summary>
    public sealed record Unavailable(AuthorUnavailable Reason, string? Detail = null, Exception? Cause = null) : TasteJudgement;
}

/// <summary>
/// The seam where Claude decides whether a written denial or a margin note is a lasting lesson or a one-off. An
/// implementation lives in Infrastructure, does the I/O, and turns every failure it can name into
/// <see cref="TasteJudgement.Unavailable"/>.
/// </summary>
public interface ITasteJudge
{
    Task<TasteJudgement> JudgeAsync(TasteEvidence evidence, CancellationToken cancellation);
}

/// <summary>What became of one piece of evidence. <see cref="Unjudged"/> can be asked again later; the other two are settled.</summary>
public enum TasteOutcome
{
    Lasting,
    OneOff,
    Unjudged,
}

/// <summary>The notes learned, and one outcome per piece of evidence, in the order it was given.</summary>
public sealed record TasteLearned(IReadOnlyList<TasteNote> Notes, IReadOnlyList<TasteOutcome> Outcomes);

/// <summary>
/// Turns evidence into lasting notes through a judge, holding the judge to what the evidence is about: a lesson must
/// make a usable note, and a lesson about a dish must be about the dish the evidence named. Silence teaches nothing —
/// no evidence, no question asked, no note.
/// </summary>
public static class TasteLearner
{
    public const int MaxLessonsPerEvidence = 3;

    public static async Task<TasteLearned> LearnAsync(IReadOnlyList<TasteEvidence> evidence, ITasteJudge judge, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(judge);
        foreach (var item in evidence)
        {
            item.EnsureValid();
        }

        var notes = new List<TasteNote>();
        var outcomes = new List<TasteOutcome>();
        foreach (var item in evidence)
        {
            var judgement = await judge.JudgeAsync(item, cancellation);
            switch (judgement)
            {
                case TasteJudgement.OneOff:
                    outcomes.Add(TasteOutcome.OneOff);
                    break;
                case TasteJudgement.Unavailable:
                    outcomes.Add(TasteOutcome.Unjudged);
                    break;
                case TasteJudgement.Lasting { Lessons.Count: 0 }:
                    outcomes.Add(TasteOutcome.OneOff);
                    break;
                case TasteJudgement.Lasting lasting:
                    var kept = lasting.Lessons.Take(MaxLessonsPerEvidence).Select(lesson => NoteFor(item, lesson)).OfType<TasteNote>().ToList();
                    foreach (var note in kept)
                    {
                        var at = notes.FindIndex(existing => existing.Person == note.Person && existing.Kind == note.Kind && existing.Subject == note.Subject);
                        if (at >= 0)
                        {
                            notes[at] = note;
                        }
                        else
                        {
                            notes.Add(note);
                        }
                    }

                    outcomes.Add(kept.Count > 0 ? TasteOutcome.Lasting : TasteOutcome.Unjudged);
                    break;
                default:
                    throw new InvalidOperationException("The taste judge returned neither a lesson, a one-off nor a reason.");
            }
        }

        return new TasteLearned(notes, outcomes);
    }

    private static TasteNote? NoteFor(TasteEvidence evidence, TasteLesson lesson)
    {
        var note = TasteNote.TryCreate(evidence.Person, lesson.Kind, lesson.Subject, lesson.Stance, TasteOrigin.Learned, evidence.Day);
        if (note is { Kind: TasteSubjectKind.Dish } && (evidence.Recipe is not { } recipe || note.Subject != recipe.Value))
        {
            return null;
        }

        return note;
    }
}
