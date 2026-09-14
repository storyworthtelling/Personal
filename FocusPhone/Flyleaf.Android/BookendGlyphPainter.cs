using Android.Content;
using Android.Graphics;
using Codex.Domain.Apps;
using Codex.Ui;

namespace Flyleaf;

/// <summary>
/// Draws the two pieces of a real bookend — shared by the tray's <see cref="BookshelfView"/> and the
/// Settings → Shelves chip, so both draw the exact same object rather than two renderings that can
/// drift apart:
/// <list type="bullet">
/// <item><see cref="DrawWall"/> — the thin upright bronze plate that actually holds the books up.</item>
/// <item><see cref="DrawStatue"/> — the shelf's glyph rendered as a solid bronze figure standing on
/// the shelf beside the wall, not a decoration on a plaque: nothing is drawn behind it, so the wood
/// (or, in Settings, the paper) shows through everywhere the figure doesn't cover. Three glyph kinds
/// (<see cref="Category.Glyph"/>): a built-in "folder_*" drawable, an owner shelf's AI vector
/// ("path:&lt;d&gt;", authored as a closed silhouette and filled), or the offline pencil monogram
/// ("mono:&lt;letters&gt;").</item>
/// </list>
/// </summary>
public static class BookendGlyphPainter
{
    /// <summary>The upright wall: a cast-bronze plate with a vertical metal gradient and a bevel
    /// highlight/shadow for relief, flush against the first book.</summary>
    public static void DrawWall(Canvas canvas, RectF wall)
    {
        using (var metal = new Paint { AntiAlias = true })
        {
            metal.SetShader(new LinearGradient(0, wall.Top, 0, wall.Bottom, PaperTheme.Bronze, PaperTheme.BronzeDeep, Shader.TileMode.Clamp!));
            var radius = Math.Min(wall.Width() * 0.3f, 3f);
            canvas.DrawRoundRect(wall, radius, radius, metal);
        }
        using (var hi = new Paint { AntiAlias = true, Color = Alpha(PaperTheme.PaperBright, 150), StrokeWidth = Math.Max(wall.Width() * 0.12f, 1f) })
        {
            hi.SetStyle(Paint.Style.Stroke);
            canvas.DrawLine(wall.Left + 1f, wall.Top + 1f, wall.Left + 1f, wall.Bottom, hi);
        }
        using var lo = new Paint { AntiAlias = true, Color = Alpha(PaperTheme.WalnutDeep, 150), StrokeWidth = Math.Max(wall.Width() * 0.1f, 1f) };
        lo.SetStyle(Paint.Style.Stroke);
        canvas.DrawLine(wall.Right - 1f, wall.Top + 2f, wall.Right - 1f, wall.Bottom, lo);
    }

    /// <summary>The statue: the shelf's glyph as a solid bronze figure. Nothing is drawn behind it —
    /// the caller's background (wood, paper) shows through everywhere the figure's own silhouette
    /// doesn't cover.</summary>
    public static void DrawStatue(Canvas canvas, Context context, RectF area, string? glyph)
    {
        if (string.IsNullOrEmpty(glyph))
        {
            return;
        }

        if (glyph.StartsWith("path:", StringComparison.Ordinal))
        {
            DrawFilledPath(canvas, glyph[5..], area);
        }
        else if (glyph.StartsWith("mono:", StringComparison.Ordinal))
        {
            DrawFilledMonogram(canvas, context, glyph[5..], area);
        }
        else
        {
            DrawFilledDrawable(canvas, context, glyph, area);
        }
    }

    private static Color Alpha(Color color, int alpha) => new(color.R, color.G, color.B, (byte)alpha);

    private static void DrawFilledDrawable(Canvas canvas, Context context, string glyph, RectF area)
    {
        var id = context.Resources!.GetIdentifier(glyph, "drawable", context.PackageName);
        var drawable = id == 0 ? null : context.GetDrawable(id);
        if (drawable is null)
        {
            return;
        }
        drawable.SetBounds((int)area.Left, (int)area.Top, (int)area.Right, (int)area.Bottom);
        drawable.SetTint(PaperTheme.BronzeDeep.ToArgb());
        drawable.Draw(canvas);
    }

    // The AI glyph is authored as a single closed silhouette (the ShelfGlyphClient prompt asks for
    // one), so it fills as one solid figure rather than stroking as a wireframe. Android's fill
    // implicitly closes an open subpath with a straight line back to its start, so a reply that
    // omits the trailing Z still fills sensibly instead of throwing or drawing nothing.
    private static void DrawFilledPath(Canvas canvas, string pathData, RectF area)
    {
        var subpaths = SvgGlyph.Parse(pathData);
        if (subpaths is null)
        {
            return; // unparseable — leave the wall standing alone
        }
        using var path = new Android.Graphics.Path();
        foreach (var polyline in subpaths)
        {
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
            path.Close();
        }
        using var fill = new Paint(PaintFlags.AntiAlias);
        fill.SetShader(new LinearGradient(0, area.Top, 0, area.Bottom, PaperTheme.Bronze, PaperTheme.BronzeDeep, Shader.TileMode.Clamp!));
        fill.SetStyle(Paint.Style.Fill);
        canvas.DrawPath(path, fill);
    }

    private static void DrawFilledMonogram(Canvas canvas, Context context, string letters, RectF area)
    {
        using var text = new Paint(PaintFlags.AntiAlias)
        {
            TextAlign = Paint.Align.Center,
            TextSize = area.Height() * 0.85f,
        };
        text.SetShader(new LinearGradient(0, area.Top, 0, area.Bottom, PaperTheme.Bronze, PaperTheme.BronzeDeep, Shader.TileMode.Clamp!));
        text.SetTypeface(PaperTheme.SerifSemibold(context));
        canvas.DrawText(letters, area.CenterX(), area.CenterY() + text.TextSize / 3f, text);
    }
}
