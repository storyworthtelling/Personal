using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals;

/// <summary>What a lasting lesson is about.</summary>
public enum TasteSubjectKind
{
    Cuisine,
    Ingredient,
    Dish,
}

public enum TasteStance
{
    Likes,
    Dislikes,
}

/// <summary>Whether the owner typed it in as a seed dislike or the judge decided it was a lasting lesson.</summary>
public enum TasteOrigin
{
    Seeded,
    Learned,
}

/// <summary>The stored words for a note's enums. <b>Frozen once shipped</b>: literals, never enum names.</summary>
public static class TasteNoteWords
{
    public static FieldWords<TasteSubjectKind> Kinds { get; } = new(new Dictionary<TasteSubjectKind, string>
    {
        [TasteSubjectKind.Cuisine] = "cuisine",
        [TasteSubjectKind.Ingredient] = "ingredient",
        [TasteSubjectKind.Dish] = "dish",
    });

    public static FieldWords<TasteStance> Stances { get; } = new(new Dictionary<TasteStance, string>
    {
        [TasteStance.Likes] = "likes",
        [TasteStance.Dislikes] = "dislikes",
    });

    public static FieldWords<TasteOrigin> Origins { get; } = new(new Dictionary<TasteOrigin, string>
    {
        [TasteOrigin.Seeded] = "seeded",
        [TasteOrigin.Learned] = "learned",
    });
}

/// <summary>
/// What the household has learned about one person's taste (kind <c>taste-note</c>): a cuisine, an ingredient or a
/// dish, liked or disliked. One record per (person, kind, subject), so changing your mind edits the one record.
/// The subject is kept lower-case with single spaces. <see cref="ToString"/> names none of it.
/// </summary>
public sealed record TasteNote
{
    public const int MaxSubjectLength = 60;

    public TasteNote(PersonId person, TasteSubjectKind kind, string subject, TasteStance stance, TasteOrigin origin, DateOnly learned)
    {
        Person = person;
        Kind = kind;
        Subject = Normalize(subject);
        Stance = stance;
        Origin = origin;
        Learned = learned;
        EnsureValid();
    }

    public PersonId Person { get; init; }

    public TasteSubjectKind Kind { get; init; }

    public string Subject { get; init; }

    public TasteStance Stance { get; init; }

    public TasteOrigin Origin { get; init; }

    public DateOnly Learned { get; init; }

    /// <summary>Throws <see cref="ArgumentException"/> naming what is wrong. Run again before writing, since <c>with</c> skips the constructor.</summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Subject) || Subject.Length > MaxSubjectLength)
        {
            throw new ArgumentException("A taste note's subject is 1 to " + MaxSubjectLength + " characters.", nameof(Subject));
        }

        switch (Kind)
        {
            case TasteSubjectKind.Cuisine when !Cuisines.Words.TryValue(Subject, out _):
                throw new ArgumentException("A cuisine note names one of the box's cuisines.", nameof(Subject));
            case TasteSubjectKind.Ingredient when TasteTokens.Of(Subject).Count == 0:
                throw new ArgumentException("An ingredient note needs a food word.", nameof(Subject));
            case TasteSubjectKind.Dish when !IsCardId(Subject):
                throw new ArgumentException("A dish note names a card by its id.", nameof(Subject));
        }
    }

    /// <summary>The note, or null when the rules refuse it. For input that is not the owner's own (a judge's answer).</summary>
    public static TasteNote? TryCreate(PersonId person, TasteSubjectKind kind, string? subject, TasteStance stance, TasteOrigin origin, DateOnly learned)
    {
        try
        {
            return new TasteNote(person, kind, subject ?? string.Empty, stance, origin, learned);
        }
        catch (ArgumentException)
        {
            // A refused subject is the expected shape of bad input here; the caller treats null as "nothing learned".
            return null;
        }
    }

    internal static string Normalize(string? subject) =>
        string.Join(' ', (subject ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public override string ToString() => nameof(TasteNote);

    private static bool IsCardId(string text)
    {
        try
        {
            _ = new RecipeId(text);
            return true;
        }
        catch (ArgumentException)
        {
            // Not a card id; the caller turns that into its own ArgumentException naming the field.
            return false;
        }
    }
}

/// <summary>The seed dislikes the owner types per person (Household, C-8.1.2), turned into the first notes.</summary>
public static class TasteSeed
{
    /// <summary>
    /// One Dislikes note per distinct word: a cuisine word becomes a cuisine note, anything else an ingredient note.
    /// Blank words and words too long to be a food are left out.
    /// </summary>
    public static IReadOnlyList<TasteNote> Notes(PersonId person, IEnumerable<string?> dislikes, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(dislikes);
        var notes = new List<TasteNote>();
        foreach (var word in dislikes)
        {
            var subject = TasteNote.Normalize(word);
            if (subject.Length == 0)
            {
                continue;
            }

            var kind = Cuisines.Words.TryValue(subject, out _) ? TasteSubjectKind.Cuisine : TasteSubjectKind.Ingredient;
            var note = TasteNote.TryCreate(person, kind, subject, TasteStance.Dislikes, TasteOrigin.Seeded, day);
            if (note is not null && !notes.Any(existing => existing.Kind == note.Kind && existing.Subject == note.Subject))
            {
                notes.Add(note);
            }
        }

        return notes;
    }
}
