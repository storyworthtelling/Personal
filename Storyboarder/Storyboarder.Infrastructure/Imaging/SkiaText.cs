using SkiaSharp;

namespace Storyboarder.Infrastructure.Imaging;

// Shared word-wrap for Skia text drawing: greedily packs words onto lines that fit maxWidth,
// hard-placing any single word that is itself wider than the box. Used by the comic page composer
// and the analysis-document renderer so both wrap text identically.
internal static class SkiaText
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
