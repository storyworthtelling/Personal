using SkiaSharp;

namespace Storyworthtelling.Paper;

/// <summary>
/// The owner's mark, <b>drawn</b> (F36): a canted oval pierced by an M whose bottom-left and
/// top-right legs push past it.
///
/// <para>It used to be a photograph — a black-oval render circle-masked into a medallion and
/// embedded as a PNG in five places. A photograph cannot take a colourway, cannot be stroked at the
/// weight a page needs, and is resampled at every size it is printed at; the approved mark is a path,
/// so it does all three.</para>
///
/// <para>DESIGN.md §12 permits the mark on a printed document and <b>nowhere in the app</b>, which is
/// why it lives in the library that prints rather than in a UI kit.</para>
///
/// <para>Shared: Folio's report cover prints it in the jacket's own colourway, and Foreword's brief
/// prints it in the page's ink — the same mark, where the brief used to carry a photograph.</para>
/// </summary>
public static class OwnersMark
{
    /// <summary>The mark's own box, the units the path below is written in.</summary>
    public const float Box = 120f;

    private const float OvalCentre = 60f;
    private const float OvalRadiusX = 47f;
    private const float OvalRadiusY = 34f;
    private const float OvalStroke = 1.3f;
    private const float LetterStroke = 4f;

    // M34 116 L46 28 L60 84 L74 2 L84 90 — the approved letterform, Mockup 01 rev 4 §02. Its first and
    // last points sit outside the oval on purpose: the legs pierce it.
    private static readonly SKPoint[] Letter =
    [
        new(34, 116), new(46, 28), new(60, 84), new(74, 2), new(84, 90),
    ];

    /// <summary>
    /// Draws the mark to fill <paramref name="dest"/>, the colours as packed 0xRRGGBB — the shape
    /// Folio's jacket reading hands back.
    /// </summary>
    public static void Draw(SKCanvas canvas, SKRect dest, int ground, int line, int ink) =>
        Draw(canvas, dest, Rgb(ground), Rgb(line), Rgb(ink));

    /// <summary>
    /// Draws the mark to fill <paramref name="dest"/>: the oval filled with <paramref name="ground"/>
    /// and edged with <paramref name="line"/>, the letter stroked in <paramref name="ink"/>.
    /// </summary>
    public static void Draw(SKCanvas canvas, SKRect dest, SKColor ground, SKColor line, SKColor ink)
    {
        canvas.Save();
        canvas.Translate(dest.Left, dest.Top);
        canvas.Scale(dest.Width / Box, dest.Height / Box);

        using (var fill = new SKPaint { Color = ground, IsAntialias = true, Style = SKPaintStyle.Fill })
            canvas.DrawOval(OvalCentre, OvalCentre, OvalRadiusX, OvalRadiusY, fill);
        using (var edge = new SKPaint
        {
            Color = line, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = OvalStroke,
        })
            canvas.DrawOval(OvalCentre, OvalCentre, OvalRadiusX, OvalRadiusY, edge);

        using var path = new SKPath();
        path.MoveTo(Letter[0]);
        for (var i = 1; i < Letter.Length; i++)
        {
            path.LineTo(Letter[i]);
        }
        using (var stroke = new SKPaint
        {
            Color = ink,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = LetterStroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        })
            canvas.DrawPath(path, stroke);

        canvas.Restore();
    }

    private static SKColor Rgb(int value) =>
        new((byte)(value >> 16 & 0xFF), (byte)(value >> 8 & 0xFF), (byte)(value & 0xFF));
}
