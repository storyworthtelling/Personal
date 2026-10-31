using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Storyworthtelling.Paper;

namespace Folio.Android.Pages;

/// <summary>
/// The desk itself: a warm plank lit from the top left, its grain illustrated rather than rendered.
///
/// <para>It fills the well and sits under everything, which is the composition the frame is for —
/// the wood is the ground, so the books lie flat and their painted covers face up, and that is the
/// one arrangement where the jackets are the main thing you see.</para>
///
/// <para><b>Nothing is ever lettered straight onto it.</b> Muted ink on this wood measures 1.40:1,
/// far under §17 — so every small label on this screen lies on a note card or a post-it, where it is
/// the same value it is everywhere else in the suite. <c>MaterialContrastTests</c> holds that.</para>
///
/// <para><b>The wood is the owner's own hand-drawn crayon-and-pencil wood grain</b> — a photo of a real
/// drawing (crayon-colored ground, loose wobbly wood-grain lines sketched over it in colored pencil),
/// cropped to just the drawn area and tiled vertically at full width (<c>Assets/desk-wood-tile.png</c>):
/// one small image repeated across the well, the same cached-and-reused shape as
/// <c>Storyworthtelling.Paper.PaperGrain</c>'s shader tile, so the desk stays consistent with
/// DESIGN.md §2 rather than embedding one giant per-screen raster. The source photo's own uneven
/// lighting was flattened and its edges healed for seamless repetition (the heal is a narrow cross-fade, not a wide blur, so no smeared cross runs through the wood) (an image model was tried
/// first and kept returning a clean engraved-line illustration no prompt could talk it out of — this
/// is the owner's real drawing instead). It is loaded once from the native Android asset (this head
/// has no <c>&lt;SingleProject&gt;</c>, so MAUI's <c>Resources/Raw</c> convention is not reliable here
/// — see the csproj) and reused for every draw. A tile that fails to load (an unusual build, not a
/// real device) falls back to the plain gradient this replaced, so a desk always paints something
/// rather than throwing mid-frame.</para>
/// </summary>
public sealed class DeskDrawable : IDrawable
{
    // The tile is scaled so that ONE copy spans the whole width of the well, at the image's own
    // aspect ratio. Horizontally it therefore never repeats at all (there is no vertical seam to see),
    // and the only repeat left is the vertical one, whose top and bottom edges join each other.
    private static readonly Lazy<Microsoft.Maui.Graphics.IImage?> Tile = new(LoadTile);

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Tile.Value is { } tile && tile.Width > 0)
        {
            var width = dirtyRect.Width;
            var height = width * tile.Height / tile.Width;

            canvas.SaveState();
            canvas.ClipRectangle(dirtyRect);
            for (var y = dirtyRect.Top; y < dirtyRect.Bottom; y += height)
            {
                canvas.DrawImage(tile, dirtyRect.Left, y, width, height);
            }
            canvas.RestoreState();
            return;
        }

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
    }

    private static Microsoft.Maui.Graphics.IImage? LoadTile()
    {
        try
        {
            using var stream = global::Android.App.Application.Context.Assets!.Open("desk-wood-tile.png");
            return PlatformImage.FromStream(stream);
        }
        catch
        {
            return null;
        }
    }
}
