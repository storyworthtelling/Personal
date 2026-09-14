using Android.Content;
using Android.Graphics;
using Android.Views;

namespace Flyleaf;

/// <summary>
/// A settings-scale rendering of a shelf's actual bookend — the same thin upright wall plus bronze
/// statue (<see cref="BookendGlyphPainter"/>) that the tray carries on the shelf, drawn small,
/// rather than a plain monogram label standing in for it.
/// </summary>
public sealed class BookendChipView : View
{
    // Proportions mirror ShelfMetrics.Default (wall/statue width and height ratios), computed against
    // this view's own measured size rather than a row height, since there's no shelf row here.
    private const float WallWidthFraction = 0.22f;
    private const float WallHeightFraction = 0.92f;
    private const float StatueHeightFraction = 0.6f;

    private readonly string glyph;

    public BookendChipView(Context context, string glyph) : base(context)
    {
        this.glyph = glyph;
    }

    protected override void OnDraw(Canvas? canvas)
    {
        base.OnDraw(canvas);
        if (canvas is null)
        {
            return;
        }

        var wallWidth = Width * WallWidthFraction;
        var wall = new RectF(Width - wallWidth, Height * (1 - WallHeightFraction), Width, Height);
        BookendGlyphPainter.DrawWall(canvas, wall);

        var statue = new RectF(0, Height * (1 - StatueHeightFraction), wall.Left, Height);
        BookendGlyphPainter.DrawStatue(canvas, Context!, statue, glyph);
    }
}
