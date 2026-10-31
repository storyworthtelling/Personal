using Constitution.Domain.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Constitution.Tests.Documents;

/// <summary>A painted colour read back from a content stream, with what it was used for.</summary>
internal sealed record PaintedColour(string Use, double R, double G, double B);

/// <summary>
/// Reads a rendered document back, so the tests hold the page to what was painted rather than to the
/// constants that were meant to produce it. Geometry comes back in page points from the top left
/// (PdfPig reports from the bottom left).
/// </summary>
internal static class PdfReadBack
{
    private static readonly string[] StrokeOperators = ["S", "s", "B", "B*", "b", "b*"];
    private static readonly string[] FillOperators = ["f", "F", "f*", "B", "B*", "b", "b*"];
    private static readonly string[] TextOperators = ["Tj", "TJ", "'", "\""];

    public static PdfDocument Open(byte[] pdf) => PdfDocument.Open(new MemoryStream(pdf));

    public static InkRect Flip(Page page, UglyToad.PdfPig.Core.PdfRectangle r) =>
        new((float)r.Left, (float)(page.Height - r.Top), (float)(r.Right - r.Left), (float)(r.Top - r.Bottom));

    /// <summary>Every painted path with its bounds, whether it was stroked, and its stroke colour.</summary>
    public static IReadOnlyList<(InkRect Bounds, bool Stroked, bool Filled, (double R, double G, double B) Stroke)> Paths(Page page)
    {
        var result = new List<(InkRect, bool, bool, (double, double, double))>();
        foreach (var path in page.Paths)
        {
            if (path.GetBoundingRectangle() is not { } rect)
            {
                continue;
            }

            var (r, g, b) = path.IsStroked ? path.StrokeColor.ToRGBValues() : (0d, 0d, 0d);
            result.Add((Flip(page, rect), path.IsStroked, path.IsFilled, (r, g, b)));
        }

        return result;
    }

    public static bool Same(InkRect a, InkRect b, float tolerance = 0.06f) =>
        Math.Abs(a.Left - b.Left) <= tolerance && Math.Abs(a.Top - b.Top) <= tolerance
        && Math.Abs(a.Width - b.Width) <= tolerance && Math.Abs(a.Height - b.Height) <= tolerance;

    public static bool SameColour((double R, double G, double B) a, (double R, double G, double B) b) =>
        Math.Abs(a.R - b.R) < 0.012 && Math.Abs(a.G - b.G) < 0.012 && Math.Abs(a.B - b.B) < 0.012;

    /// <summary>
    /// Walks the content stream's colour operators (RG, rg, G, g) and records the colour in force at
    /// every painting operator. Skia sets the stroke and fill together, so a stroke-only path still
    /// carries a fill colour in the stream; only the colour a paint operator actually used counts.
    /// An operator this walker does not understand fails loudly instead of being skipped, because a
    /// skipped colour operator is exactly how a coloured mark would slip past.
    /// </summary>
    public static IReadOnlyList<PaintedColour> PaintedColours(Page page)
    {
        var stroke = (R: 0d, G: 0d, B: 0d);
        var fill = (R: 0d, G: 0d, B: 0d);
        var painted = new List<PaintedColour>();

        foreach (var operation in page.Operations)
        {
            var op = operation.Operator;
            switch (op)
            {
                case "RG":
                case "G":
                    stroke = Numbers(operation.ToString(), op);
                    break;
                case "rg":
                case "g":
                    fill = Numbers(operation.ToString(), op);
                    break;
                case "K" or "k" or "SC" or "SCN" or "sc" or "scn" or "CS" or "cs":
                    throw new NotSupportedException(
                        $"The page uses the colour operator '{op}', which this read-back does not decode. "
                        + "A document drawn for the tablets paints in plain RGB or grey only.");
            }

            if (StrokeOperators.Contains(op))
            {
                painted.Add(new PaintedColour("stroke", stroke.R, stroke.G, stroke.B));
            }

            if (FillOperators.Contains(op))
            {
                painted.Add(new PaintedColour("fill", fill.R, fill.G, fill.B));
            }

            if (TextOperators.Contains(op))
            {
                painted.Add(new PaintedColour("text", fill.R, fill.G, fill.B));
            }
        }

        return painted;
    }

    private static (double R, double G, double B) Numbers(string operation, string op)
    {
        var parts = operation.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part != op)
            .Select(part => double.Parse(part, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        return parts.Length switch
        {
            1 => (parts[0], parts[0], parts[0]),
            3 => (parts[0], parts[1], parts[2]),
            _ => throw new FormatException($"Could not read the {op} operands from '{operation}'."),
        };
    }

    public static (double R, double G, double B) Rgb(SkiaSharp.SKColor colour) =>
        (colour.Red / 255.0, colour.Green / 255.0, colour.Blue / 255.0);

    public static (double R, double G, double B) Rgb(string hex) => Rgb(SkiaSharp.SKColor.Parse(hex));
}
