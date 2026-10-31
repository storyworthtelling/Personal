using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals;

/// <summary>What one person said about one dinner: the three lanes on the rating page (D14, D37).</summary>
public enum TasteVerdict
{
    Loved,
    Fine,
    NeverAgain,
}

/// <summary>
/// The stored words and the printed lane words for <see cref="TasteVerdict"/>. <b>The stored words are frozen once
/// shipped</b> (literals, never enum names). The printed words are what the rating page draws: "Loved", "Fine",
/// "Never again".
/// </summary>
public static class TasteWords
{
    public static FieldWords<TasteVerdict> Verdicts { get; } = new(new Dictionary<TasteVerdict, string>
    {
        [TasteVerdict.Loved] = "loved",
        [TasteVerdict.Fine] = "fine",
        [TasteVerdict.NeverAgain] = "never-again",
    });

    /// <summary>
    /// The verdict a printed lane word means, compared without case and with a dash or run of spaces read as one
    /// space ("Never again", "never-again"). Any other word is no verdict; a lane is never guessed.
    /// </summary>
    public static TasteVerdict? FromPrinted(string? printed)
    {
        if (string.IsNullOrWhiteSpace(printed))
        {
            return null;
        }

        var words = string.Join(' ', printed.Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToLowerInvariant();
        return words switch
        {
            "loved" => TasteVerdict.Loved,
            "fine" => TasteVerdict.Fine,
            "never again" => TasteVerdict.NeverAgain,
            _ => null,
        };
    }
}

/// <summary>
/// One person's rating of one dish on one day (kind <c>rating</c>). The household file keeps one record per
/// (person, card, day), so rating the same dish the same day twice is the same record, not two. The written reason is
/// optional and is read by the judge only; the profile never keeps it. <see cref="ToString"/> names none of it.
/// </summary>
public sealed record TasteRating
{
    public const int MaxReasonLength = 500;

    public TasteRating(PersonId person, RecipeId recipe, DateOnly day, TasteVerdict verdict, string? reason = null)
    {
        Person = person;
        Recipe = recipe;
        Day = day;
        Verdict = verdict;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        EnsureValid();
    }

    public PersonId Person { get; init; }

    public RecipeId Recipe { get; init; }

    public DateOnly Day { get; init; }

    public TasteVerdict Verdict { get; init; }

    public string? Reason { get; init; }

    /// <summary>Throws <see cref="ArgumentException"/> naming what is wrong. Run again before writing, since <c>with</c> skips the constructor.</summary>
    public void EnsureValid()
    {
        if (Reason is { Length: > MaxReasonLength })
        {
            throw new ArgumentException("A reason is at most " + MaxReasonLength + " characters.", nameof(Reason));
        }
    }

    public override string ToString() => nameof(TasteRating);
}
