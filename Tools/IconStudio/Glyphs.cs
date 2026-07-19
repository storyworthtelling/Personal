using SkiaSharp;

namespace IconStudio;

/// <summary>
/// The six ink line-art glyphs, each with exactly one terracotta detail
/// (DESIGN.md §8). Every painter draws on a normalized square canvas of the
/// given size, keeping content inside the central adaptive-icon safe zone.
/// </summary>
public static class Glyphs
{
    // Relative stroke width: reads like a 2dp mark at icon scale and survives
    // the 48px legacy sizes.
    private const float StrokeRel = 0.035f;

    private static SKPaint InkStroke(float size) => new()
    {
        Style = SKPaintStyle.Stroke,
        StrokeWidth = size * StrokeRel,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
        Color = Tokens.Ink,
        IsAntialias = true,
    };

    private static SKPaint AccentStroke(float size) => new()
    {
        Style = SKPaintStyle.Stroke,
        StrokeWidth = size * StrokeRel,
        StrokeCap = SKStrokeCap.Round,
        Color = Tokens.Accent,
        IsAntialias = true,
    };

    private static SKPaint AccentFill() => new()
    {
        Style = SKPaintStyle.Fill,
        Color = Tokens.Accent,
        IsAntialias = true,
    };

    /// <summary>FocusPhone launcher: clock face, terracotta hour hand.</summary>
    public static void Clock(SKCanvas canvas, float s)
    {
        using var ink = InkStroke(s);
        using var accent = AccentStroke(s);
        var c = s / 2;
        canvas.DrawCircle(c, c, s * 0.24f, ink);
        canvas.DrawLine(c, c, c, c - s * 0.16f, ink);            // minute hand, up
        canvas.DrawLine(c, c, c - s * 0.10f, c + s * 0.06f, accent); // hour hand, ~7 o'clock
    }

    /// <summary>Camera: lens ring, terracotta aperture dot.</summary>
    public static void Lens(SKCanvas canvas, float s)
    {
        using var ink = InkStroke(s);
        using var accent = AccentFill();
        var c = s / 2;
        canvas.DrawCircle(c, c, s * 0.24f, ink);
        canvas.DrawCircle(c, c, s * 0.135f, ink);
        canvas.DrawCircle(c + s * 0.155f, c - s * 0.155f, s * 0.035f, accent);
    }

    /// <summary>Audio: waveform bars, one terracotta.</summary>
    public static void Waveform(SKCanvas canvas, float s)
    {
        using var ink = InkStroke(s);
        using var accent = AccentStroke(s);
        var c = s / 2;
        float[] halfHeights = [0.07f, 0.14f, 0.24f, 0.11f, 0.17f];
        var spacing = s * 0.11f;
        var startX = c - 2 * spacing;
        for (var i = 0; i < halfHeights.Length; i++)
        {
            var x = startX + i * spacing;
            var half = s * halfHeights[i];
            canvas.DrawLine(x, c - half, x, c + half, i == 2 ? accent : ink);
        }
    }

    /// <summary>Communication: speech bubble outline, terracotta dot.</summary>
    public static void Speech(SKCanvas canvas, float s)
    {
        using var ink = InkStroke(s);
        using var accent = AccentFill();
        var c = s / 2;
        var bubble = new SKRect(c - s * 0.24f, c - s * 0.19f, c + s * 0.24f, c + s * 0.13f);
        using var path = new SKPath();
        path.AddRoundRect(bubble, s * 0.07f, s * 0.07f);
        // Tail from the bubble's lower left toward the safe-zone edge.
        path.MoveTo(c - s * 0.10f, c + s * 0.13f);
        path.LineTo(c - s * 0.13f, c + s * 0.24f);
        path.LineTo(c - s * 0.02f, c + s * 0.13f);
        canvas.DrawPath(path, ink);
        canvas.DrawCircle(c, c - s * 0.03f, s * 0.035f, accent);
    }

    /// <summary>Storyboarder: comic panel grid, one terracotta panel.</summary>
    public static void PanelGrid(SKCanvas canvas, float s)
    {
        using var ink = InkStroke(s);
        using var accent = AccentFill();
        var c = s / 2;
        var half = s * 0.24f;
        var gap = s * 0.045f;
        var radius = s * 0.02f;

        var left = c - half;
        var top = c - half;
        var cellW = (2 * half - gap) / 2;
        // An asymmetric comic grid: tall left panel, two stacked right panels.
        var leftPanel = new SKRect(left, top, left + cellW, top + 2 * half);
        var rightTop = new SKRect(left + cellW + gap, top, left + 2 * half, top + cellW);
        var rightBottom = new SKRect(left + cellW + gap, top + cellW + gap, left + 2 * half, top + 2 * half);

        canvas.DrawRoundRect(leftPanel, radius, radius, ink);
        canvas.DrawRoundRect(rightTop, radius, radius, accent);
        canvas.DrawRoundRect(rightBottom, radius, radius, ink);
    }

    /// <summary>Tally: four ink tally strokes struck through by the terracotta fifth mark.</summary>
    public static void Tally(SKCanvas canvas, float s)
    {
        using var ink = InkStroke(s);
        using var accent = AccentStroke(s);
        var c = s / 2;
        var half = s * 0.16f;
        var spacing = s * 0.10f;
        var startX = c - 1.5f * spacing;
        for (var i = 0; i < 4; i++)
        {
            var x = startX + i * spacing;
            canvas.DrawLine(x, c - half, x, c + half, ink);
        }
        // The fifth mark: a terracotta diagonal striking through the group.
        canvas.DrawLine(startX - spacing * 0.4f, c + half, startX + 3 * spacing + spacing * 0.4f, c - half, accent);
    }

    /// <summary>DailyLoader: folded sheet, terracotta dog-ear.</summary>
    public static void FoldedPaper(SKCanvas canvas, float s)
    {
        using var ink = InkStroke(s);
        using var accent = AccentFill();
        var c = s / 2;
        var left = c - s * 0.19f;
        var right = c + s * 0.19f;
        var top = c - s * 0.24f;
        var bottom = c + s * 0.24f;
        var ear = s * 0.12f;

        using var sheet = new SKPath();
        sheet.MoveTo(left, top);
        sheet.LineTo(right - ear, top);
        sheet.LineTo(right, top + ear);
        sheet.LineTo(right, bottom);
        sheet.LineTo(left, bottom);
        sheet.Close();
        canvas.DrawPath(sheet, ink);

        using var fold = new SKPath();
        fold.MoveTo(right - ear, top);
        fold.LineTo(right - ear, top + ear);
        fold.LineTo(right, top + ear);
        fold.Close();
        canvas.DrawPath(fold, accent);

        canvas.DrawLine(left + s * 0.06f, c - s * 0.06f, right - s * 0.06f, c - s * 0.06f, ink);
        canvas.DrawLine(left + s * 0.06f, c + s * 0.04f, right - s * 0.06f, c + s * 0.04f, ink);
        canvas.DrawLine(left + s * 0.06f, c + s * 0.14f, c + s * 0.04f, c + s * 0.14f, ink);
    }
}
