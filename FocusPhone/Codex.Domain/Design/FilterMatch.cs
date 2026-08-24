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
    /// </summary>
    private static string Fold(string? value)
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
