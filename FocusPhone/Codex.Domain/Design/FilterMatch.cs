using System.Globalization;
using System.Text;

namespace Codex.Domain.Design;

/// <summary>
/// What the live filter (DESIGN.md §7) counts as a match. Pure, because it runs on
/// every keystroke over a whole library and is the one piece of that control worth
/// being sure about.
///
/// Case- and <b>diacritic</b>-insensitive substring. The diacritics matter more than
/// they look: tags carry them (Bartók, Sigur Rós, Café Tacvba) and nobody types one
/// to find a record, so a strict comparison silently hides music the owner owns.
/// </summary>
public static class FilterMatch
{
    /// <summary>
    /// How many rows fit a screen, near enough — a list-row minimum of 56dp against
    /// the content area a phone leaves under the header and above the bars.
    /// </summary>
    public const int ScreenfulRows = 12;

    /// <summary>
    /// Whether a list is long enough to earn a filter field above it (DESIGN.md §7).
    ///
    /// A fixed threshold rather than a real measurement, deliberately. Measuring would
    /// make a list at the boundary gain and lose its field as items came and went;
    /// a constant makes the field's presence something you learn once per screen
    /// instead of checking every time.
    /// </summary>
    public static bool IsWarranted(int rowCount) => rowCount > ScreenfulRows;

    /// <summary>
    /// Does this field match the query? An empty query matches everything — the
    /// filter is not narrowing yet, so nothing should disappear.
    /// </summary>
    public static bool Matches(string? candidate, string query)
    {
        var needle = Fold(query);
        if (needle.Length == 0)
        {
            return true;
        }

        return candidate is not null
            && Fold(candidate).Contains(needle, StringComparison.Ordinal);
    }

    /// <summary>
    /// Is this field exactly the value, ignoring case and accents? The equality
    /// counterpart to <see cref="Matches"/>, sharing its normalizer so a playlist
    /// rule and a filter field can never disagree about what "Bartók" is. An empty
    /// value matches nothing: unlike a filter, a rule with no value is not a rule.
    /// </summary>
    public static bool Equals(string? candidate, string value)
    {
        var wanted = Fold(value);
        return wanted.Length != 0
            && candidate is not null
            && string.Equals(Fold(candidate), wanted, StringComparison.Ordinal);
    }

    /// <summary>
    /// Does any of these fields match? What a row actually asks — a track is a hit
    /// on its title, its artist or its album, and the caller should not have to
    /// spell that out three times.
    /// </summary>
    public static bool MatchesAny(string query, params string?[] fields)
    {
        if (Fold(query).Length == 0)
        {
            return true;
        }

        foreach (var field in fields)
        {
            if (Matches(field, query))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lower-case, trimmed, and stripped of combining marks — "Bartók" and "bartok"
    /// fold to the same thing. Decomposing first is what separates a letter from its
    /// accent so the accent can be dropped.
    ///
    /// Public because it is the suite's one normal form: anything deriving a stable
    /// key for a title or an artist has to fold it the same way this matcher does, or
    /// the key and the comparison quietly disagree about what is the same record.
    /// </summary>
    public static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
