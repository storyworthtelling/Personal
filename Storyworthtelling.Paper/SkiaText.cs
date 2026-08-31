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
}
