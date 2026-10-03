using System.Text.RegularExpressions;
using IconStudio;
using SkiaSharp;

namespace IconStudio.Tests;

/// <summary>
/// Phase 4, "Cover": Index's corner flourish is the mockup's SVG — a bracket that <b>bulges toward the
/// corner</b>, a second, fainter arc inside it, a small curl at its foot and a dot — not the quarter
/// of a circle that bulged the other way. The mockup's own path data is read out of
/// <c>Ledger/design/mockup-01.html</c> and rendered independently with Skia; the glyph IconStudio draws
/// must agree with it pixel for pixel in the top-left corner, so the drawing is held to the design
/// book rather than to a second hand copy of its numbers.
/// </summary>
public class IndexFlourishTests
{
    private const float Size = 432f;

    // The flourish occupies a square this fraction of the cover wide, this far in from the corner
    // (well inside FullBleedInset so the window stays clear).
    private const float MarginRel = 0.020f;
    private const float BoxRel = 0.100f;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Ledger", "design", "mockup-01.html")))
        {
            dir = dir.Parent;
        }
        return dir!.FullName;
    }

    private static string TopLeftSvg()
    {
        var html = File.ReadAllText(Path.Combine(RepoRoot(), "Ledger", "design", "mockup-01.html"));
        return Regex.Match(html, "<svg class=\"fl tl\".*?</svg>", RegexOptions.Singleline).Value;
    }

    private static List<string> PathData(string svg) =>
        Regex.Matches(svg, "<path d=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

    private static SKBitmap RenderReference()
    {
        var svg = TopLeftSvg();
        var paths = PathData(svg);
        var bitmap = new SKBitmap((int)Size, (int)Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var unit = Size * BoxRel / 34f;
        canvas.Translate(Size * MarginRel, Size * MarginRel);
        canvas.Scale(unit);

        for (var i = 0; i < paths.Count; i++)
        {
            using var path = SKPath.ParseSvgPathData(paths[i]);
            using var stroke = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                StrokeCap = SKStrokeCap.Round,
                Color = Tokens.Paper.WithAlpha(i == 1 ? (byte)(255 * 0.6f) : (byte)255),
                IsAntialias = true,
            };
            canvas.DrawPath(path, stroke);
        }

        var circle = Regex.Match(svg, "<circle cx=\"([\\d.]+)\" cy=\"([\\d.]+)\" r=\"([\\d.]+)\"");
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = Tokens.Paper, IsAntialias = true };
        canvas.DrawCircle(
            float.Parse(circle.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(circle.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(circle.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture), fill);
        return bitmap;
    }

    private static SKBitmap RenderGlyph()
    {
        var spec = IconSpecs.All.Single(s => s.App == "Index");
        var bitmap = new SKBitmap((int)Size, (int)Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        spec.Glyph(canvas, Size);
        return bitmap;
    }

    [Fact]
    public void TheMockupCarriesTheFourPartsTheTestReads()
    {
        var svg = TopLeftSvg();

        Assert.Equal(3, PathData(svg).Count); // the bracket, the second arc, the curl
        Assert.Contains("<circle", svg, StringComparison.Ordinal); // the dot
    }

    [Fact]
    public void TheTopLeftFlourishIsTheMockupsDrawing()
    {
        using var reference = RenderReference();
        using var actual = RenderGlyph();

        // Compared inside the corner only, short of the window's keyline (which starts at about 0.115).
        var limit = (int)(Size * 0.112f);
        var referenceInk = 0;
        var mismatched = 0;
        for (var y = 0; y < limit; y++)
        {
            for (var x = 0; x < limit; x++)
            {
                var want = reference.GetPixel(x, y).Alpha;
                var got = actual.GetPixel(x, y).Alpha;
                if (want > 96)
                {
                    referenceInk++;
                }
                if (Math.Abs(want - got) > 96)
                {
                    mismatched++;
                }
            }
        }

        Assert.True(referenceInk > 200, "the reference drew almost nothing; the probe is broken");
        Assert.True(
            mismatched < referenceInk * 0.15,
            $"{mismatched} of {referenceInk} flourish pixels differ from the mockup's SVG — the bracket is not the mockup's drawing");
    }

    [Fact]
    public void TheBracketBulgesTowardTheCorner_NotAwayFromIt()
    {
        using var actual = RenderGlyph();
        var unit = Size * BoxRel / 34f;
        var origin = Size * MarginRel;

        // Midpoint of the mockup's first cubic (M2 32 C2 14 14 2 32 2) is (10.25, 10.25): on the diagonal,
        // nearer the corner than the chord's midpoint (17, 17). The inverted quadrant had ink at the
        // chord's side and none here.
        var nearCorner = actual.GetPixel((int)(origin + 10.25f * unit), (int)(origin + 10.25f * unit)).Alpha;
        var chordMid = actual.GetPixel((int)(origin + 17f * unit), (int)(origin + 17f * unit)).Alpha;

        Assert.True(nearCorner > 128, $"no ink where the bracket passes near the corner (alpha {nearCorner})");

        // A quarter and three quarters of the way along: (4.16, 19.72) hugs the left edge, (19.72, 4.16)
        // the top one — the bracket leaves each edge steeply and rounds the corner, which a circle's
        // quadrant (the old drawing) does not.
        var low = actual.GetPixel((int)(origin + 4.16f * unit), (int)(origin + 19.72f * unit)).Alpha;
        var high = actual.GetPixel((int)(origin + 19.72f * unit), (int)(origin + 4.16f * unit)).Alpha;
        Assert.True(low > 128 && high > 128, $"the bracket's shoulders are missing (alphas {low}, {high})");
        Assert.True(chordMid < 32, $"ink at the chord's midpoint (alpha {chordMid}) — the bracket sags away from the corner");
    }

    [Fact]
    public void EveryCornerIsAReflectionOfTheTopLeft_NeverARotation()
    {
        using var actual = RenderGlyph();
        var limit = (int)(Size * 0.112f);
        var mismatched = 0;
        for (var y = 0; y < limit; y++)
        {
            for (var x = 0; x < limit; x++)
            {
                var here = actual.GetPixel(x, y).Alpha;
                var right = actual.GetPixel((int)Size - 1 - x, y).Alpha;
                var below = actual.GetPixel(x, (int)Size - 1 - y).Alpha;
                var opposite = actual.GetPixel((int)Size - 1 - x, (int)Size - 1 - y).Alpha;
                if (Math.Abs(here - right) > 24 || Math.Abs(here - below) > 24 || Math.Abs(here - opposite) > 24)
                {
                    mismatched++;
                }
            }
        }

        Assert.True(mismatched < 60, $"{mismatched} pixels break the mirror lines");
    }
}
