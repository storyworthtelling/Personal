namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// The words a denied evening is kept and listed under (D111). The stored word is a frozen lower-case weekday ("tuesday"), written out so that renaming a
/// <see cref="DayOfWeek"/> member can never rename a word already in a decision record; the shopping line is that weekday and "dinner" ("Tuesday dinner"),
/// a single plain line with no dish and no ingredient.
/// </summary>
public static class DeniedEveningWords
{
    /// <summary>The words an unplanned evening says about itself in the kept week; Today and This week draw it with their existing nothing-planned rendering.</summary>
    public const string NotApprovedReason = "Not approved.";

    private static readonly string[] Words = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

    /// <summary>The frozen word for a weekday.</summary>
    public static string Of(DayOfWeek day) => Words[(int)day];

    /// <summary>The weekday a stored word names; false for a word this version does not know.</summary>
    public static bool TryDay(string? word, out DayOfWeek day)
    {
        var index = Array.IndexOf(Words, word);
        day = index < 0 ? default : (DayOfWeek)index;
        return index >= 0;
    }

    /// <summary>The shopping line for one evening: "Tuesday dinner".</summary>
    /// <exception cref="ArgumentException">The word is not a weekday word.</exception>
    public static string Line(string word)
    {
        if (!TryDay(word, out var day))
        {
            throw new ArgumentException("That is not one of the weekday words a denied evening is kept under.", nameof(word));
        }

        return char.ToUpperInvariant(word[0]) + word[1..] + " dinner";
    }

    /// <summary>The shopping lines for these evenings, once each, in the order given. A word this version does not know is left out rather than guessed at.</summary>
    public static IReadOnlyList<string> Lines(IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        return words.Where(word => TryDay(word, out _)).Distinct(StringComparer.Ordinal).Select(Line).ToList();
    }
}
