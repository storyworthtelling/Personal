using Android.Graphics;
using Watch.Domain.Design;
using Watch.Domain.Kit;

namespace Watch.Ui.Wear;

/// <summary>Draws Dash's glyph (WD §3, family A): one short horizontal dash with round ends, glowing in its colour.</summary>
internal static class GlyphPainter
{
    public static void Dash(Canvas canvas, float centreX, float centreY, float side, Oklch colour, double alpha)
    {
        using var paint = WatchPaints.Stroke(colour, (float)(side * GlyphGeometry.DashStroke), alpha, (float)(side * GlyphGeometry.Glow));
        var left = centreX - side / 2;
        canvas.DrawLine(left + (float)(side * GlyphGeometry.DashFrom), centreY, left + (float)(side * GlyphGeometry.DashTo), centreY, paint);
    }
}
