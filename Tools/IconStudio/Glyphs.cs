using SkiaSharp;

namespace IconStudio;

/// <summary>
/// The seven ink line-art glyphs, each with exactly one terracotta detail
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

    /// <summary>
    /// Score: a keyboard curving through the frame, bleeding off both edges so it
    /// reads as a fragment of something larger rather than an object on a ground.
    ///
    /// <b>Generated, not painted.</b> Within an octave a black key sits after white
    /// keys 0,1 and 3,4,5 — and not after 2 or 6, which is what gives a keyboard its
    /// two-then-three grouping and the wide E–F and B–C gaps. Image models cannot
    /// count, so every painted attempt spaced the black keys evenly, which is
    /// musically wrong and unusable to a pianist. Drawing it makes it correct by
    /// construction.
    ///
    /// White keys are cream rather than white — paper, not plastic, per DESIGN.md §2.
    /// </summary>
    public static void Keyboard(SKCanvas canvas, float s)
    {
        // The keys the black ones follow, within an octave of seven whites.
        HashSet<int> blackAfter = [0, 1, 3, 4, 5];

        const int Whites = 22;          // enough that the ends run off the frame
        var depth = s * 0.19f;          // front-to-back, along the curve's normal
        var centreY = s * 0.52f;
        var amplitude = s * 0.118f;
        const float Periods = 1.05f;    // just over one wave across the frame
        const float Phase = -0.55f;

        using var white = new SKPaint { Style = SKPaintStyle.Fill, Color = Tokens.Paper, IsAntialias = true };
        using var black = new SKPaint { Style = SKPaintStyle.Fill, Color = Tokens.Ink, IsAntialias = true };
        using var seam = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = s * 0.0022f * 2.2f,
            Color = Tokens.Ink.WithAlpha(128),
            IsAntialias = true,
        };

        (SKPoint Point, SKPoint Normal) At(float u)
        {
            var x = u * s;
            var k = 2f * MathF.PI * Periods / s;
            var y = centreY + (amplitude * MathF.Sin((k * x) + Phase));
            var slope = amplitude * k * MathF.Cos((k * x) + Phase);
            var length = MathF.Sqrt(1f + (slope * slope));
            return (new SKPoint(x, y), new SKPoint(-slope / length, 1f / length));
        }

        static SKPoint Offset(SKPoint p, SKPoint normal, float distance) =>
            new(p.X + (normal.X * distance), p.Y + (normal.Y * distance));

        static void Quad(SKPath path, SKPoint a, SKPoint b, SKPoint c, SKPoint d)
        {
            path.MoveTo(a);
            path.LineTo(b);
            path.LineTo(c);
            path.LineTo(d);
            path.Close();
        }

        using var whites = new SKPath();
        using var seams = new SKPath();
        for (var i = 0; i < Whites; i++)
        {
            var (p0, n0) = At((float)i / Whites);
            var (p1, n1) = At((float)(i + 1) / Whites);
            var back0 = Offset(p0, n0, -depth / 2f);
            var back1 = Offset(p1, n1, -depth / 2f);
            Quad(whites, back0, back1, Offset(p1, n1, depth / 2f), Offset(p0, n0, depth / 2f));

            if (i > 0)
            {
                seams.MoveTo(back0);
                seams.LineTo(Offset(p0, n0, depth / 2f));
            }
        }

        canvas.DrawPath(whites, white);
        canvas.DrawPath(seams, seam);

        var blackWidth = 1f / Whites * 0.58f;
        var blackDepth = depth * 0.62f;
        using var blacks = new SKPath();
        for (var i = 0; i < Whites - 1; i++)
        {
            if (!blackAfter.Contains(i % 7))
            {
                continue;
            }

            var boundary = (float)(i + 1) / Whites;
            var (pa, na) = At(boundary - (blackWidth / 2f));
            var (pc, nc) = At(boundary + (blackWidth / 2f));
            Quad(blacks,
                Offset(pa, na, -depth / 2f),
                Offset(pc, nc, -depth / 2f),
                Offset(pc, nc, (-depth / 2f) + blackDepth),
                Offset(pa, na, (-depth / 2f) + blackDepth));
        }

        canvas.DrawPath(blacks, black);
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

    /// <summary>Canonizer: a proofreader's red pen — an ink nib with a terracotta ink bead at the tip.</summary>
    public static void PenNib(SKCanvas canvas, float s)
    {
        using var ink = InkStroke(s);
        using var accent = AccentFill();
        var c = s / 2;

        // The nib body: a downward-pointing shape with a flat, rounded shoulder at top
        // tapering to the writing point. Vertical extent is centered in the safe zone.
        var shoulderY = c - 0.20f * s;
        var tipY = c + 0.23f * s;
        var halfW = 0.15f * s;

        using var body = new SKPath();
        body.MoveTo(c - halfW, shoulderY);
        body.LineTo(c + halfW, shoulderY);
        body.LineTo(c, tipY);
        body.Close();
        canvas.DrawPath(body, ink);

        // The central slit running down the nib from below the breather hole to just above the point.
        canvas.DrawLine(c, c - 0.05f * s, c, c + 0.14f * s, ink);
        // The breather hole near the top of the nib.
        canvas.DrawCircle(c, c - 0.10f * s, 0.03f * s, ink);

        // The one terracotta detail: a bead of ink pooled at the writing point.
        canvas.DrawCircle(c, c + 0.17f * s, 0.04f * s, accent);
    }

    /// <summary>Ledger: four ink ledger strokes struck through by the terracotta fifth mark.</summary>
    public static void Ledger(SKCanvas canvas, float s)
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

    /// <summary>Flyleaf: a cream ex-libris bookplate (arch-into-study window + title rule),
    /// terracotta maker's mark. Drawn on the warm identity ground the icon spec sets.</summary>
    public static void Bookplate(SKCanvas canvas, float s)
    {
        using var ink = InkStroke(s);
        using var accent = AccentFill();
        using var cream = new SKPaint { Style = SKPaintStyle.Fill, Color = Tokens.Paper, IsAntialias = true };
        using var rule = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = s * 0.014f,
            StrokeJoin = SKStrokeJoin.Round,
            Color = Tokens.Ink,
            IsAntialias = true,
        };

        // The cream plate (a book cover), portrait, centered in the safe zone with a warm margin.
        var plate = new SKRect(s * 0.29f, s * 0.22f, s * 0.71f, s * 0.78f);
        var r = s * 0.028f;
        canvas.DrawRoundRect(plate, r, r, cream);

        // A single fine ex-libris rule just inside the edge.
        canvas.DrawRoundRect(Inset(plate, s * 0.035f), r * 0.6f, r * 0.6f, rule);

        // The arch-into-study window (the hero motif): a rounded-top opening.
        float archLeft = s * 0.375f, archRight = s * 0.625f, archTop = s * 0.31f, archBottom = s * 0.55f;
        var archR = (archRight - archLeft) / 2f;
        using var arch = new SKPath();
        arch.MoveTo(archLeft, archBottom);
        arch.LineTo(archLeft, archTop + archR);
        arch.ArcTo(new SKRect(archLeft, archTop, archRight, archTop + 2 * archR), 180, 180, false);
        arch.LineTo(archRight, archBottom);
        canvas.DrawPath(arch, ink);

        // The lower title band: a short rule with the terracotta maker's mark beneath it.
        canvas.DrawLine(s * 0.40f, s * 0.63f, s * 0.60f, s * 0.63f, ink);
        canvas.DrawCircle(s * 0.5f, s * 0.71f, s * 0.026f, accent);
    }

    private static SKRect Inset(SKRect r, float d) => new(r.Left + d, r.Top + d, r.Right - d, r.Bottom - d);
}
