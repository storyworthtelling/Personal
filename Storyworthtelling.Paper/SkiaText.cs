using SkiaSharp;

namespace Storyworthtelling.Paper;

// Shared word-wrap for Skia text drawing: greedily packs words onto lines that fit maxWidth,
// hard-placing any single word that is itself wider than the box. Used by every app's page/report
// renderers so they wrap text identically.
public static class SkiaText
{
    public static List<string> Wrap(SKFont font, string text, float maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (current.Length == 0 || font.MeasureText(candidate) <= maxWidth)
                current = candidate;
            else
            {
                lines.Add(current);
                current = word;
            }
        }

        if (current.Length > 0)
            lines.Add(current);

        return lines.Count == 0 ? [string.Empty] : lines;
    }

    /// <summary>
    /// The same wrap, but reporting where each line sits in the original string.
    ///
    /// <para><see cref="Wrap"/> splits on spaces and rejoins, which loses the offsets — fine for a
    /// paragraph of report prose, useless for a manuscript a proofreading mark has to land on. Here
    /// every line is a half-open span into <paramref name="text"/>, so a mark's own span can be
    /// intersected with it. A newline is a hard break, because a paragraph the author wrote is not a
    /// place the wrap may decide about.</para>
    /// </summary>
    public static List<TextSpan> WrapSpans(SKFont font, string text, float maxWidth)
    {
        var lines = new List<TextSpan>();
        if (string.IsNullOrEmpty(text))
            return lines;

        var paragraphStart = 0;
        while (paragraphStart <= text.Length)
        {
            var newline = text.IndexOf('\n', paragraphStart);
            var paragraphEnd = newline < 0 ? text.Length : newline;
            WrapParagraph(font, text, paragraphStart, paragraphEnd, maxWidth, lines);

            if (newline < 0)
                break;
            paragraphStart = newline + 1;
        }

        return lines;
    }

    private static void WrapParagraph(
        SKFont font, string text, int from, int to, float maxWidth, List<TextSpan> lines)
    {
        if (to <= from)
        {
            lines.Add(new TextSpan(from, from));   // a blank line the author put there
            return;
        }

        var lineStart = -1;
        var lineEnd = from;
        var i = from;

        while (i < to)
        {
            while (i < to && text[i] == ' ')
                i++;
            if (i >= to)
                break;

            var wordEnd = i;
            while (wordEnd < to && text[wordEnd] != ' ')
                wordEnd++;

            if (lineStart < 0)
            {
                lineStart = i;
                lineEnd = wordEnd;
            }
            else if (font.MeasureText(text[lineStart..wordEnd]) <= maxWidth)
            {
                lineEnd = wordEnd;
            }
            else
            {
                lines.Add(new TextSpan(lineStart, lineEnd));
                lineStart = i;
                lineEnd = wordEnd;
            }

            i = wordEnd;
        }

        if (lineStart >= 0)
            lines.Add(new TextSpan(lineStart, lineEnd));
    }
}

/// <summary>A half-open range of a string: the characters from <c>Start</c> up to <c>End</c>.</summary>
public readonly record struct TextSpan(int Start, int End)
{
    public int Length => Math.Max(End - Start, 0);
}
