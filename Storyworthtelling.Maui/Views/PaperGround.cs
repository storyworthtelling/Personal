using Microsoft.Maui.Graphics.Platform;
using Storyworthtelling.Materials.Android;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// A paper ground (DESIGN.md §2): the flat ground colour with the paper grain over it, the same tile every native
/// screen's window background and every document carries, drawn through the materials engine's one painter.
/// </summary>
public sealed class PaperGroundDrawable : IDrawable
{
    public Color Ground { get; set; } = PaperResources.Colour("Paper");

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (canvas is PlatformCanvas platform && platform.Canvas is { } native)
        {
            // A GraphicsView's canvas is already in dp; the grain is one texel a device pixel, as it is natively.
            var density = (float)DeviceDisplay.Current.MainDisplayInfo.Density;
            MaterialPainters.PaperGround(
                native,
                new global::Android.Graphics.RectF(dirtyRect.Left, dirtyRect.Top, dirtyRect.Right, dirtyRect.Bottom),
                Ground.AsColor(),
                density > 0 ? 1 / density : 1);
            return;
        }

        canvas.FillColor = Ground;
        canvas.FillRectangle(dirtyRect);
    }
}

/// <summary>A view that is only a paper ground, laid behind what sits on it. It takes no touches.</summary>
public sealed class PaperGround : GraphicsView
{
    private readonly PaperGroundDrawable painter = new();

    public PaperGround(Color ground)
    {
        painter.Ground = ground;
        Drawable = painter;
        InputTransparent = true;
        BackgroundColor = Colors.Transparent;
    }

    public Color Ground
    {
        get => painter.Ground;
        set
        {
            painter.Ground = value;
            Invalidate();
        }
    }
}
