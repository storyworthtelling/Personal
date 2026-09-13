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
    // Where the grain runs, as fractions of the plank's height, with the sag of each curve. Fixed
    // rather than generated: the frame drew nine lines and this is those nine.
    private static readonly (float Y, float Sag)[] Grain =
    [
        (0.076f, -0.008f), (0.174f, 0.012f), (0.282f, -0.009f), (0.387f, 0.013f), (0.492f, -0.010f),
        (0.600f, 0.011f), (0.703f, -0.010f), (0.809f, 0.012f), (0.914f, -0.011f),
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

        // The long grain, running with the plank.
        canvas.StrokeColor = grain.WithAlpha(0.24f);
        canvas.StrokeSize = 1.4f;
        foreach (var (y, sag) in Grain)
        {
            var top = dirtyRect.Top + (dirtyRect.Height * y);
            var path = new PathF();
            path.MoveTo(dirtyRect.Left - 4, top);
            path.QuadTo(
                dirtyRect.Center.X, top + (dirtyRect.Height * sag),
                dirtyRect.Right + 4, top + (dirtyRect.Height * sag * -0.2f));
            canvas.DrawPath(path);
        }

        // Two boards' worth of seam, so the plank is not one impossible slab.
        canvas.StrokeColor = grain.WithAlpha(0.14f);
        canvas.StrokeSize = 1f;
        foreach (var x in new[] { 0.19f, 0.71f })
        {
            var at = dirtyRect.Left + (dirtyRect.Width * x);
            var seam = new PathF();
            seam.MoveTo(at, dirtyRect.Top - 4);
            seam.QuadTo(at + 8, dirtyRect.Center.Y, at - 6, dirtyRect.Bottom + 4);
            canvas.DrawPath(seam);
        }
    }
}
