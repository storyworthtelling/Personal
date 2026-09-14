using Android.Content;
using Android.Graphics;
using Android.Views;
using Codex.Ui;

namespace Flyleaf;

/// <summary>
/// A settings-scale rendering of a shelf's actual bookend — the same low, wide bronze block with its
/// engraved glyph (<see cref="BookendGlyphPainter"/>) that the tray carries on the shelf, drawn small,
/// rather than a plain monogram label standing in for it.
/// </summary>
public sealed class BookendChipView : View
{
    private readonly float density;
    private readonly string glyph;

    public BookendChipView(Context context, string glyph) : base(context)
    {
        this.glyph = glyph;
        density = context.Resources!.DisplayMetrics!.Density;
    }

    protected override void OnDraw(Canvas? canvas)
    {
        base.OnDraw(canvas);
        if (canvas is null)
        {
            return;
        }

        var r = new RectF(0, 0, Width, Height);
        using (var metal = new Paint { AntiAlias = true })
        {
            metal.SetShader(new LinearGradient(0, r.Top, 0, r.Bottom, PaperTheme.Bronze, PaperTheme.BronzeDeep, Shader.TileMode.Clamp!));
            canvas.DrawRoundRect(r, 3 * density, 3 * density, metal);
        }

        var s = Math.Min(r.Width(), r.Height()) * 0.72f;
        var gx = r.CenterX() - s / 2;
        var gy = r.CenterY() - s / 2;
        BookendGlyphPainter.Draw(canvas, Context!, density, new RectF(gx, gy, gx + s, gy + s), glyph, PaperTheme.BronzeDeep);
    }
}
