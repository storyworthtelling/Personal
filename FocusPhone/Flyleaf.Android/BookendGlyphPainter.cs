using Android.Content;
using Android.Graphics;
using Codex.Domain.Apps;

namespace Flyleaf;

/// <summary>
/// Draws a shelf's engraved bookend glyph — extracted out of <see cref="BookshelfView"/> so the tray's
/// bookend and the Settings → Shelves chip render the exact same medallion for a category rather than
/// two renderings that can drift apart. Three glyph kinds (<see cref="Category.Glyph"/>): a built-in
/// "folder_*" drawable, an owner shelf's AI vector ("path:&lt;d&gt;"), or the offline pencil monogram
/// ("mono:&lt;letters&gt;").
/// </summary>
public static class BookendGlyphPainter
{
    public static void Draw(Canvas canvas, Context context, float density, RectF area, string? glyph, Color inkColor)
    {
        if (string.IsNullOrEmpty(glyph))
        {
            return;
        }

        if (glyph.StartsWith("path:", StringComparison.Ordinal))
        {
            DrawPath(canvas, glyph[5..], area, density, inkColor);
        }
        else if (glyph.StartsWith("mono:", StringComparison.Ordinal))
        {
            DrawMonogram(canvas, context, glyph[5..], area, inkColor);
        }
        else
        {
            DrawDrawable(canvas, context, glyph, area, inkColor);
        }
    }

    private static void DrawDrawable(Canvas canvas, Context context, string glyph, RectF area, Color inkColor)
    {
        var id = context.Resources!.GetIdentifier(glyph, "drawable", context.PackageName);
        var drawable = id == 0 ? null : context.GetDrawable(id);
        if (drawable is null)
        {
            return;
        }
        drawable.SetBounds((int)area.Left, (int)area.Top, (int)area.Right, (int)area.Bottom);
        drawable.SetTint(inkColor.ToArgb());
        drawable.Draw(canvas);
    }

    private static void DrawPath(Canvas canvas, string pathData, RectF area, float density, Color inkColor)
    {
        var subpaths = SvgGlyph.Parse(pathData);
        if (subpaths is null)
        {
            return; // unparseable — leave the medallion plain
        }
        using var stroke = new Paint(PaintFlags.AntiAlias)
        {
            Color = inkColor,
            StrokeWidth = 2 * density,
            StrokeCap = Paint.Cap.Round,
            StrokeJoin = Paint.Join.Round,
        };
        stroke.SetStyle(Paint.Style.Stroke);
        foreach (var polyline in subpaths)
        {
            using var path = new Android.Graphics.Path();
            for (var i = 0; i < polyline.Count; i++)
            {
                var x = area.Left + polyline[i].X / SvgGlyph.ViewBox * area.Width();
                var y = area.Top + polyline[i].Y / SvgGlyph.ViewBox * area.Height();
                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }
            canvas.DrawPath(path, stroke);
        }
    }

    private static void DrawMonogram(Canvas canvas, Context context, string letters, RectF area, Color inkColor)
    {
        using var text = new Paint(PaintFlags.AntiAlias)
        {
            Color = inkColor,
            TextAlign = Paint.Align.Center,
            TextSize = area.Height() * 0.7f,
        };
        text.SetTypeface(Codex.Ui.PaperTheme.SerifSemibold(context));
        canvas.DrawText(letters, area.CenterX(), area.CenterY() + text.TextSize / 3f, text);
    }
}
