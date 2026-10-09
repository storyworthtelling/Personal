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

    /// <summary>Foreword: folded sheet, terracotta dog-ear.</summary>
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

    /// <summary>
    /// The window every cover icon now uses: wide enough that the art reaches the edge of the
    /// round mask, so the icon reads as a picture rather than as a picture inside a frame.
    ///
    /// <para>It began as Foreword's exception — see <see cref="CoverKeyline"/> — and became the
    /// rule when the owner chose it for the rest. At this inset an adaptive launcher's mask crops
    /// <i>inside</i> the keyline, so on the home screen neither the keyline nor the cloth is
    /// visible; the ground is still declared, because it is what a launcher shows behind the
    /// foreground during its zoom and parallax.</para>
    /// </summary>
    public const float FullBleedInset = 0.12f;

    /// <summary>
    /// The one thing drawn around a cover: the window's own edge, and nothing else.
    ///
    /// <para>Every cover icon used to carry four mitred corner brackets as well. Foreword was the
    /// first to drop them, and the reason was arithmetic rather than taste: an adaptive icon's
    /// round mask keeps only the central 72 of 108, so framing survives only while it stays
    /// inside that circle, and the engraving had to be large to be legible at 48dp — a doorstep,
    /// a rolled newspaper and a door all inside a third of the tile read as a smudge. At the
    /// inset that made the picture big enough, brackets would have been sliced off by the mask,
    /// and broken framing reads as a rendering fault where no framing reads as a picture.</para>
    ///
    /// <para>The owner then chose that shape for the rest, so it is now the rule and the
    /// brackets are gone from the repo. What is left is the keyline, on exactly the rect the
    /// renderer put the art in, so it reads as the window's own edge rather than as a border
    /// floating near it.</para>
    ///
    /// <para>Measured, so the claim is not larger than the fact: at this inset the mask crops
    /// <i>inside</i> the keyline, so on the phone's home screen an icon reads as a full-bleed
    /// picture and neither the keyline nor the cloth is visible. The keyline does show on the
    /// legacy square PNG, at its very edge. The ground is still declared: it is what a launcher
    /// shows behind the foreground during its zoom and parallax, and what any consumer of
    /// values/colors.xml reads.</para>
    /// </summary>
    public static void CoverKeyline(SKCanvas canvas, float s, SKColor frameColor)
    {
        using var cream = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = s * 0.011f,
            StrokeCap = SKStrokeCap.Butt,
            StrokeJoin = SKStrokeJoin.Miter,
            Color = frameColor,
            IsAntialias = true,
        };

        // Exactly the rect the renderer put the art in, so the keyline reads as the window's own
        // edge rather than as a border floating near it.
        var window = new SKRect(
            s * FullBleedInset, s * FullBleedInset,
            s * (1f - FullBleedInset), s * (1f - FullBleedInset));
        canvas.DrawRect(window, cream);
    }

    /// <summary>Foreword's cover: cream keyline on the sunrise ground.</summary>
    public static void ForewordPlate(SKCanvas canvas, float s) => CoverKeyline(canvas, s, Tokens.Paper);


    /// <summary>Post's cover: cream keyline on the deep indigo binding.</summary>
    public static void PostPlate(SKCanvas canvas, float s) => CoverKeyline(canvas, s, Tokens.Paper);

    /// <summary>
    /// Folio's cover, as the launcher icon: the same anatomy as Post's, framed in cream for the
    /// same reason — Folio's binding is a deep red cloth, which cream draws on and ink would
    /// disappear into.
    ///
    /// <para><b>The art set into this window is a crop, and that is the point.</b> The approved
    /// cover paints a whole desk from above — monitor, keyboard, notebook, magnifier, mug — and the
    /// design book records that it does not survive being shrunk: five objects dissolve at 48dp.
    /// Going full bleed buys room but not that much room, so the picture is reduced to its subject
    /// as well as enlarged.</para>
    ///
    /// <para>So the picture is reduced to its subject instead — the composition book with the
    /// magnifier lying on it, which is what the app does, with the desk it sat on left behind. The
    /// crop is taken from the approved painting rather than drawn afresh, so nothing about the
    /// identity changes; there is simply less of it, larger.</para>
    /// </summary>
    public static void FolioPlate(SKCanvas canvas, float s) => CoverKeyline(canvas, s, Tokens.Paper);

    /// <summary>
    /// Plate's cover, as the launcher icon. The same anatomy as the rest, with the keyline
    /// <b>inverted to ink</b>: Plate's binding is the LIGHT ground tier, where Post's is deep
    /// indigo, so a cream keyline would vanish into the cloth instead of drawing the window.
    /// DESIGN.md §12 sets the binding per app and lets it contrast its own art; Plate's art is
    /// a near-black lens, which is why the cloth is light and the keyline is dark.
    /// </summary>
    public static void PlatePlate(SKCanvas canvas, float s) => CoverKeyline(canvas, s, Tokens.Ink);

    /// <summary>
    /// Pulp's cover, as the launcher icon (D267): nothing at all over the painting. The one cover
    /// without a keyline, by the owner's decision — the den is already framed by its own projection
    /// screen, and a keyline around a picture of a screen reads as a second screen.
    /// </summary>
    public static void PulpPlate(SKCanvas canvas, float s)
    {
    }

    /// <summary>
    /// Index's cover (D115, D117, D119): the same keyline-only window every cover ships, plus
    /// the one thing D119 adds — a corner flourish (the mockup's bracket, bulging toward the corner), drawn once for the
    /// top-left and <b>reflected</b>, never rotated, into the other three (an asymmetric mark
    /// rotated instead of mirrored has no mirror line and reads as a pinwheel — the reason
    /// <c>PlateFramingMirrorsOnBothAxes</c> exists). Both the keyline and the flourish are drawn
    /// in <paramref name="frameColor"/>'s own cream, so the flourish reads as the window's own
    /// line-work rather than a second colour set beside it.
    ///
    /// <para>The flourishes sit entirely inside <see cref="FullBleedInset"/>'s own outer margin
    /// — closer to the edge than the window itself, which is what "the window inset to clear
    /// them" means — so the round launcher mask crops them exactly the way it already crops the
    /// keyline: invisible on the launcher, visible on the shelf cover and the legacy square,
    /// where the full square shows (D119's own reasoning for why the round icon differs).</para>
    /// </summary>
    public static void IndexPlate(SKCanvas canvas, float s)
    {
        CoverKeyline(canvas, s, Tokens.Paper);
        DrawCornerFlourish(canvas, s, 0, 0, flipX: false, flipY: false, Tokens.Paper);
        DrawCornerFlourish(canvas, s, s, 0, flipX: true, flipY: false, Tokens.Paper);
        DrawCornerFlourish(canvas, s, 0, s, flipX: false, flipY: true, Tokens.Paper);
        DrawCornerFlourish(canvas, s, s, s, flipX: true, flipY: true, Tokens.Paper);
    }

    // Well inside FullBleedInset (0.12): the flourish's 34-unit square sits MarginRel in from the corner
    // and is BoxRel wide, so it ends at 0.12 and every stroke stays in the corner square the window
    // never enters (mockup 1's own .fl is 34px on a 300px cover; this is the same drawing, scaled).
    private const float FlourishMarginRel = 0.020f;
    private const float FlourishBoxRel = 0.100f;

    // The mockup's SVG (Ledger/design/mockup-01.html, ".shelfcover .fl.tl", viewBox 0 0 34 34), path data
    // verbatim: a bracket that BULGES TOWARD THE CORNER, a second arc inside it drawn at 60%, a curl at
    // the foot, and a dot.
    private const float FlourishUnits = 34f;
    private const string FlourishBracket = "M2 32 C2 14 14 2 32 2";
    private const string FlourishSecondArc = "M8 32 C8 18 18 8 32 8";
    private const string FlourishCurl = "M2 32 C6 30 8 26 8 22 C8 18 5 16 3 18 C1 20 3 23 6 22";
    private const float FlourishDotX = 14f;
    private const float FlourishDotY = 14f;
    private const float FlourishDotRadius = 1.6f;
    private const float FlourishStroke = 1.5f;
    private const float FlourishSecondArcOpacity = 0.6f;

    // Draws the top-left flourish, then mirrors it into position for whichever corner
    // (pivotX, pivotY) names — a reflection about that corner's own axis/axes, never a rotation.
    private static void DrawCornerFlourish(SKCanvas canvas, float s, float pivotX, float pivotY, bool flipX, bool flipY, SKColor color)
    {
        canvas.Save();
        canvas.Translate(pivotX, pivotY);
        canvas.Scale(flipX ? -1 : 1, flipY ? -1 : 1);
        DrawFlourishTopLeft(canvas, s, color);
        canvas.Restore();
    }

    // The corner bracket (D119) in the mockup's own drawing — see the constants above. It never reaches
    // the corner point itself; the bracket leaves each edge steeply and rounds toward the corner.
    private static void DrawFlourishTopLeft(SKCanvas canvas, float s, SKColor color)
    {
        canvas.Save();
        canvas.Translate(s * FlourishMarginRel, s * FlourishMarginRel);
        canvas.Scale(s * FlourishBoxRel / FlourishUnits);

        using var bracket = SKPath.ParseSvgPathData(FlourishBracket);
        using var secondArc = SKPath.ParseSvgPathData(FlourishSecondArc);
        using var curl = SKPath.ParseSvgPathData(FlourishCurl);

        using var line = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = FlourishStroke,
            StrokeCap = SKStrokeCap.Round,
            Color = color,
            IsAntialias = true,
        };
        using var faint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = FlourishStroke,
            StrokeCap = SKStrokeCap.Round,
            Color = color.WithAlpha((byte)(color.Alpha * FlourishSecondArcOpacity)),
            IsAntialias = true,
        };
        using var dot = new SKPaint { Style = SKPaintStyle.Fill, Color = color, IsAntialias = true };

        canvas.DrawPath(bracket, line);
        canvas.DrawPath(secondArc, faint);
        canvas.DrawPath(curl, line);
        canvas.DrawCircle(FlourishDotX, FlourishDotY, FlourishDotRadius, dot);
        canvas.Restore();
    }

    private static SKRect Inset(SKRect r, float d) => new(r.Left + d, r.Top + d, r.Right - d, r.Bottom - d);
}
