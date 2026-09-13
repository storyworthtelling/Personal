using Microsoft.Maui.Graphics;
using Storyworthtelling.Paper;

namespace Folio.Android.Pages;

/// <summary>
/// The desk itself: a warm plank lit from the top left, its grain drawn rather than rendered.
///
/// <para>It fills the well and sits under everything, which is the composition the frame is for —
/// the wood is the ground, so the books lie flat and their painted covers face up, and that is the
/// one arrangement where the jackets are the main thing you see.</para>
///
/// <para><b>Nothing is ever lettered straight onto it.</b> Muted ink on this wood measures 1.40:1,
/// far under §17 — so every small label on this screen lies on a note card or a post-it, where it is
/// the same value it is everywhere else in the suite. <c>MaterialContrastTests</c> holds that.</para>
///
/// <para>The grain is drawn in the same marker-and-pencil hand as the shelf's ledge, so the two
/// areas read as one world rather than two apps stitched together. It is deterministic: a desk that
/// re-grained itself on every launch would read as movement.</para>
/// </summary>
public sealed class DeskDrawable : IDrawable
{
    /// <summary>
    /// Where the grain runs, as a fraction of the plank's height: how far down it starts, how much
    /// it wanders, and how strongly it takes the pencil.
    ///
    /// <para>Fixed rather than generated — a desk that re-grained itself on every launch would read
    /// as movement. The <b>varying</b> weight is what matters: drawn at one opacity and one spacing,
    /// nine parallel lines stop reading as wood and start reading as ruled paper, and with the board
    /// seams on top of them, as tiles. Wood is irregular, and the irregularity has to be in the
    /// drawing rather than left to the eye.</para>
    /// </summary>
    private static readonly (float Y, float Wander, float Weight)[] Grain =
    [
        (0.061f, 0.010f, 0.22f), (0.139f, -0.008f, 0.13f), (0.207f, 0.013f, 0.26f),
        (0.298f, -0.011f, 0.10f), (0.372f, 0.009f, 0.21f), (0.455f, 0.014f, 0.15f),
        (0.548f, -0.012f, 0.24f), (0.617f, 0.008f, 0.11f), (0.704f, -0.013f, 0.20f),
        (0.791f, 0.011f, 0.14f), (0.878f, -0.009f, 0.23f),
    ];

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();
        canvas.SetFillPaint(
            new LinearGradientPaint
            {
                StartColor = Color.FromArgb(MaterialTokens.DeskWoodLightHex).WithAlpha(0.80f),
                EndColor = Color.FromArgb(MaterialTokens.DeskWoodDeepHex).WithAlpha(0.88f),
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
            },
            dirtyRect);
        canvas.FillRectangle(dirtyRect);
        canvas.RestoreState();

        var grain = Color.FromArgb(MaterialTokens.DeskGrainHex);
        canvas.StrokeLineCap = LineCap.Round;

        // Each line an S rather than a single bend: one curve is an arc, and eleven parallel arcs
        // are a pattern. Two opposed curves wander.
        foreach (var (y, wander, weight) in Grain)
        {
            var top = dirtyRect.Top + (dirtyRect.Height * y);
            var drift = dirtyRect.Height * wander;

            canvas.StrokeColor = grain.WithAlpha(weight);
            canvas.StrokeSize = weight > 0.18f ? 1.4f : 1f;

            var path = new PathF();
            path.MoveTo(dirtyRect.Left - 4, top);
            path.QuadTo(dirtyRect.Left + (dirtyRect.Width * 0.3f), top + drift,
                dirtyRect.Center.X, top + (drift * 0.35f));
            path.QuadTo(dirtyRect.Left + (dirtyRect.Width * 0.75f), top - (drift * 0.55f),
                dirtyRect.Right + 4, top + (drift * 0.15f));
            canvas.DrawPath(path);
        }

        // One board seam, not two, and faint. Two evenly spaced verticals across regular grain is
        // what turned the desk into a tiled floor.
        canvas.StrokeColor = grain.WithAlpha(0.07f);
        canvas.StrokeSize = 1f;
        var at = dirtyRect.Left + (dirtyRect.Width * 0.62f);
        var seam = new PathF();
        seam.MoveTo(at, dirtyRect.Top - 4);
        seam.QuadTo(at + 7, dirtyRect.Center.Y, at - 5, dirtyRect.Bottom + 4);
        canvas.DrawPath(seam);
    }
}
