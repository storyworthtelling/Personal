using Microsoft.Maui.Graphics;
using Index.Domain.Model;

namespace Index.Android.Views;

/// <summary>
/// Paints a card's disclosure chevron (mockups 2 and 3's <c>.ix-cv</c>): a 14 x 14 stroke, round-capped, in
/// the muted ink — the shape <see cref="CardChevron"/> names, drawn rather than typeset, so no font decides
/// what it looks like. The geometry (the paths, the size, the stroke) is the Domain's; this only strokes it.
/// </summary>
public sealed class ChevronDrawable : IDrawable
{
    public ChevronDrawable(string token, Color ink)
    {
        Token = token;
        Ink = ink;
    }

    /// <summary>A token from <see cref="CardChevron"/> (<see cref="CardChevron.Down"/> or <see cref="CardChevron.Up"/>).</summary>
    public string Token { get; }

    public Color Ink { get; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;
        canvas.StrokeColor = Ink;
        canvas.StrokeSize = (float)CardChevron.StrokeDp;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        // The view is exactly SizeDp square, so the path's own 14 x 14 box maps one to one.
        canvas.DrawPath(PathBuilder.Build(CardChevron.PathOf(Token)));
    }
}
