using Microsoft.Maui.Graphics;

namespace Index.Android.Views;

/// <summary>
/// Repeating horizontal rules at a fixed pitch — the index card's blue body lines and (shared, since
/// it is the identical mechanism) any other ruled-paper surface this head draws. A thin painter;
/// callers size the view to an exact multiple of <see cref="PitchDp"/> so the last rule lands flush
/// with the bottom rather than being cut mid-line.
/// </summary>
public sealed class RuledLinesDrawable : IDrawable
{
    public Color RuleColour { get; set; } = Colors.Transparent;

    public float PitchDp { get; set; } = 22f;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.StrokeColor = RuleColour;
        canvas.StrokeSize = 1f;

        // Rules sit at the BOTTOM of each pitch-tall row (mockup-02's `.ix-body` repeating-gradient
        // draws the line at the end of each 22px step), so text baselines land just above one.
        for (var y = PitchDp; y <= dirtyRect.Height + 0.5f; y += PitchDp)
        {
            canvas.DrawLine(0, y, dirtyRect.Width, y);
        }
    }
}
