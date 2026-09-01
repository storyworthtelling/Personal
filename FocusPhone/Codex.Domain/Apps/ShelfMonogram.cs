namespace Codex.Domain.Apps;

/// <summary>
/// The offline fallback for an owner-created shelf's bookend glyph (feature #11). When an
/// AI-generated vector glyph can't be produced — no network or no shared model key — the
/// bookend is engraved with a pencil monogram drawn from the shelf name instead: the initials
/// of its first two words, or a single word's first letter. Pure so it's unit-tested without a
/// device and identical whichever head renders it.
/// </summary>
public static class ShelfMonogram
{
    /// <summary>One or two uppercase letters (or digits) for the shelf, or "?" when the name
    /// carries no letters or digits to draw from.</summary>
    public static string For(string name)
    {
        var initials = Tokenize(name ?? string.Empty)
            .Take(2)
            .Select(token => char.ToUpperInvariant(token[0]));
        var monogram = new string(initials.ToArray());
        return monogram.Length == 0 ? "?" : monogram;
    }

    // Maximal runs of letters/digits are the words; punctuation and whitespace separate them.
    private static IEnumerable<string> Tokenize(string name)
    {
        var start = -1;
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsLetterOrDigit(name[i]))
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                yield return name[start..i];
                start = -1;
            }
        }
        if (start >= 0)
        {
            yield return name[start..];
        }
    }
}
