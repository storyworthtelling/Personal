using Microsoft.Maui.Graphics;
using Storyworthtelling.Paper;

namespace Folio.Android.Pages;

/// <summary>
/// The empty end of the desk: a pencil lying across it, and the ring a cup left.
///
/// <para>It is not decoration for its own sake. The desk is a drawn surface, and a drawn surface
/// that stops dead under the last book reads as a texture that ran out rather than as a desk. Two
/// marks in the same pencil as the grain are enough to say the wood continues.</para>
///
/// <para>Drawn in the grain's own ink at low opacity, so it never competes with anything on paper
/// above it — and it carries no information, so nothing is lost when it is scrolled past.</para>
/// </summary>
public sealed class DeskFillerDrawable : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var pencil = Color.FromArgb(MaterialTokens.DeskGrainHex);
        canvas.StrokeLineCap = LineCap.Round;

        // A pencil, lying at a slight angle with its sharpened end to the right.
        canvas.StrokeColor = pencil.WithAlpha(0.5f);
        canvas.StrokeSize = 1.6f;
        var body = new PathF();
        body.MoveTo(34, 96);
        body.LineTo(40, 90);
        body.LineTo(152, 72);
        body.LineTo(150, 78);
        body.Close();
        canvas.DrawPath(body);

        canvas.StrokeColor = pencil.WithAlpha(0.38f);
        canvas.DrawLine(34, 96, 150, 78);

        var tip = new PathF();
        tip.MoveTo(150, 78);
        tip.LineTo(163, 74);
        tip.LineTo(152, 72);
        tip.Close();
        canvas.FillColor = pencil.WithAlpha(0.30f);
        canvas.FillPath(tip);

        // Two rings and a handle: a cup that was here a moment ago.
        canvas.StrokeColor = pencil.WithAlpha(0.38f);
        canvas.StrokeSize = 1.6f;
        canvas.DrawCircle(228, 104, 21);

        canvas.StrokeColor = pencil.WithAlpha(0.22f);
        canvas.DrawCircle(228, 104, 15);

        canvas.StrokeColor = pencil.WithAlpha(0.3f);
        var handle = new PathF();
        handle.MoveTo(249, 98);
        handle.QuadTo(260, 104, 249, 111);
        canvas.DrawPath(handle);
    }
}
