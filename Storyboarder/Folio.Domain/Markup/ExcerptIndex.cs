using System.Text;

namespace Folio.Domain.Markup;

/// <summary>
/// Finds a quoted passage in the manuscript it was quoted from, and says where it is.
///
/// <para><b>An exact search does not work</b>, which is the whole reason this exists. A quotation
/// comes back through a model and a JSON string: a line break in the manuscript is a space in the
/// excerpt, runs of spaces collapse, and a typographic quote may have become a straight one. Searched
/// literally, most excerpts are simply not found, and the marked-up manuscript comes out nearly
/// blank — which looks like a book with no problems rather than a search that failed.</para>
///
/// <para>So the manuscript is indexed in a normalised form with a map back to its real offsets, and
/// the excerpt is normalised the same way. What is <i>not</i> done is any fuzzy matching: an excerpt
/// that still does not appear is dropped. A mark on approximately the right sentence is worse than
/// no mark, because the page gives the author no way to tell.</para>
/// </summary>
public sealed class ExcerptIndex
{
    private readonly string normalised;
    private readonly int[] offsets;

    private ExcerptIndex(string normalised, int[] offsets)
    {
        this.normalised = normalised;
        this.offsets = offsets;
    }

    public static ExcerptIndex Of(string manuscript)
    {
        ArgumentNullException.ThrowIfNull(manuscript);

        var builder = new StringBuilder(manuscript.Length);
        var map = new List<int>(manuscript.Length);
        var lastWasSpace = true;   // leading whitespace is dropped

        for (var i = 0; i < manuscript.Length; i++)
        {
            var character = Fold(manuscript[i]);
            if (character == ' ')
            {
                if (lastWasSpace)
                    continue;
                lastWasSpace = true;
            }
            else
            {
                lastWasSpace = false;
            }

            builder.Append(character);
            map.Add(i);
        }

        return new ExcerptIndex(builder.ToString(), [.. map]);
    }

    /// <summary>Where the excerpt sits in the original text, or null when it is not there.</summary>
    public TextRange? Find(string? excerpt)
    {
        if (string.IsNullOrWhiteSpace(excerpt))
        {
            return null;
        }

        var needle = Normalise(excerpt);
        if (needle.Length == 0)
        {
            return null;
        }

        var at = normalised.IndexOf(needle, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var start = offsets[at];
        var end = offsets[at + needle.Length - 1] + 1;
        return new TextRange(start, end);
    }

    private static string Normalise(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lastWasSpace = true;

        foreach (var raw in text)
        {
            var character = Fold(raw);
            if (character == ' ')
            {
                if (lastWasSpace)
                    continue;
                lastWasSpace = true;
            }
            else
            {
                lastWasSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Whitespace becomes one space and the typographic marks a word processor substitutes become
    /// their plain forms — the two ways a quotation reliably stops matching the page it came from.
    /// </summary>
    private static char Fold(char character) => character switch
    {
        '‘' or '’' => '\'',
        '“' or '”' => '"',
        '–' or '—' => '-',
        ' ' => ' ',
        _ => char.IsWhiteSpace(character) ? ' ' : character,
    };
}

/// <summary>A half-open range of the manuscript.</summary>
public readonly record struct TextRange(int Start, int End);
