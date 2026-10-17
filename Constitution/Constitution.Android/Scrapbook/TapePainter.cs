using Android.Graphics;
using Constitution.Domain.Scraps;
using Storyworthtelling.Paper;

namespace Constitution.Scrapbook;

/// <summary>
/// The strip of Scotch tape that holds a scrap (D48): translucent cream with a faint edge, lying
/// across the scrap's top edge at its own slight turn. Where it goes is <see cref="TapeAnchor"/>,
/// already resolved by <see cref="ScrapLayout"/> with the scrap's tilt in it, so this draws in the
/// well's space rather than the scrap's and the tape is never turned twice.
/// </summary>
public sealed class TapePainter
{
    private const float EdgeDp = 1;

    private readonly Paint strip = ScrapInk.Fill(ScrapInk.Tape, ScrapInk.Alpha(MaterialTokens.TapeAlpha));
    private readonly Paint edge = ScrapInk.Stroke(ScrapInk.TapeEdge, EdgeDp, ScrapInk.Alpha(MaterialTokens.TapeEdgeAlpha));

    /// <summary>Draws one strip. The canvas is in dp with its origin at the well's top-left.</summary>
    public void Draw(Canvas canvas, TapeAnchor tape)
    {
        var halfWidth = (float)(tape.WidthDp / 2);
        var halfHeight = (float)(tape.HeightDp / 2);
        var half = EdgeDp / 2;

        canvas.Save();
        canvas.Translate((float)tape.CenterX, (float)tape.CenterY);
        canvas.Rotate((float)tape.RotationDegrees);
        canvas.DrawRect(-halfWidth, -halfHeight, halfWidth, halfHeight, strip);
        canvas.DrawRect(-halfWidth + half, -halfHeight + half, halfWidth - half, halfHeight - half, edge);
        canvas.Restore();
    }
}
