using Microsoft.Maui.Graphics;
using Index.Domain.Design;

namespace Index.Android.Views;

/// <summary>
/// D122: the graph-paper ground every Accounts card lies on — two tiers of line in the identity
/// ground colour (mockup-02.html's <c>.dv</c> background-image: a 16dp square grid with a heavier
/// line every 64dp), each an ALPHA fraction on <see cref="MaterialTokens"/>'s registered constants
/// rather than a flat colour, so <c>GraphPaperContrastTests</c> is checking the same composited pixel
/// this drawable actually paints.
///
/// <para>A thin painter over <see cref="MainPage"/>'s own bounds — it holds no state and is safe to
/// invalidate on every layout pass; <c>SizeChanged</c> triggers a repaint the same way
/// <see cref="Storyworthtelling.Maui.Views.BinderTabs"/> does.</para>
/// </summary>
public sealed class GraphPaperBackground : GraphicsView
{
    public GraphPaperBackground()
    {
        Drawable = new Painter();
        SizeChanged += (_, _) => Invalidate();
    }

    /// <summary>The identity ground colour the lines are drawn in — Index's own D115/D117 mark, read
    /// from <see cref="Pages.LedgerAreas"/>'s registered ladder by the caller rather than duplicated
    /// here (this view has no opinion about which app's identity it is drawing).</summary>
    public Color GroundColour
    {
        get => ((Painter)Drawable!).Ground;
        set
        {
            ((Painter)Drawable!).Ground = value;
            Invalidate();
        }
    }

    private sealed class Painter : IDrawable
    {
        public Color Ground { get; set; } = Colors.Transparent;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeSize = 1f;

            DrawGrid(canvas, dirtyRect, MaterialTokens.GraphPaperMinorDp, (float)MaterialTokens.GraphPaperMinorAlpha);
            DrawGrid(canvas, dirtyRect, MaterialTokens.GraphPaperMajorDp, (float)MaterialTokens.GraphPaperMajorAlpha);
        }

        private void DrawGrid(ICanvas canvas, RectF rect, int pitch, float alpha)
        {
            canvas.StrokeColor = Ground.WithAlpha(alpha);

            for (var x = 0f; x < rect.Width; x += pitch)
            {
                canvas.DrawLine(x, 0, x, rect.Height);
            }
            for (var y = 0f; y < rect.Height; y += pitch)
            {
                canvas.DrawLine(0, y, rect.Width, y);
            }
        }
    }
}
