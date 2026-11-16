using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Storyworthtelling.Materials.Android;

namespace Codex.Ui;

/// <summary>
/// A tab glyph (<c>Storyworthtelling.Materials.TabGlyphs</c>) in colored pencil (DESIGN.md §2, §10): its 2dp
/// stroke with the pencil's tooth, in one ink. The tooth is a cached shader, so drawing it costs no more than a
/// plain vector drawable did. Set <see cref="Ink"/> to re-ink it; a colour filter would flatten the tooth.
/// </summary>
public sealed class PencilGlyphDrawable(Context context, string glyph) : Drawable
{
    private readonly float density = context.Resources!.DisplayMetrics!.Density;
    private Color ink = PaperTheme.Ink;

    public Color Ink
    {
        get => ink;
        set
        {
            ink = value;
            InvalidateSelf();
        }
    }

    public override int IntrinsicWidth => (int)Math.Round(Storyworthtelling.Materials.TabGlyphs.Viewport * density);

    public override int IntrinsicHeight => IntrinsicWidth;

    public override int Opacity => (int)Format.Translucent;

    public override void Draw(Canvas canvas)
    {
        var bounds = Bounds;
        var side = Math.Min(bounds.Width(), bounds.Height());
        var box = new RectF(bounds.Left, bounds.Top, bounds.Left + side, bounds.Top + side);
        MaterialPainters.PencilGlyph(canvas, glyph, box, ink, density);
    }

    public override void SetAlpha(int alpha)
    {
    }

    public override void SetColorFilter(ColorFilter? colorFilter)
    {
    }
}
