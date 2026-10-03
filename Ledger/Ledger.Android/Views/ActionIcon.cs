using Microsoft.Maui.Graphics;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>Which stroke icon an action-card row carries (mockups 2 and 4's <c>.dv-card .act svg</c>).</summary>
public enum ActionIconKind
{
    MoveUp,
    MoveDown,
    MoveToBottom,
    Disconnect,
    Bank,
    Pencil,
}

/// <summary>
/// The 18 x 18 stroke icon at the head of an action-card row: 1.6 wide, round caps and joins, in the row's
/// own ink (the mockup's <c>currentColor</c>). The path data is the mockup's own, so the glyph is the one
/// the design book drew.
/// </summary>
public sealed class ActionIcon : GraphicsView
{
    public const int SizeDp = 18;
    private const float StrokeDp = 1.6f;

    // The mockups' 18 x 18 viewBox paths, verbatim.
    private static string PathOf(ActionIconKind kind) => kind switch
    {
        ActionIconKind.MoveUp => "M9 14V4M5 8l4-4 4 4",
        ActionIconKind.MoveDown => "M9 4v10M5 10l4 4 4-4",
        ActionIconKind.MoveToBottom => "M9 3v10M5 9l4 4 4-4M4 15.5h10",
        ActionIconKind.Disconnect =>
            "M7 11l4-4M6 8.5 4.5 10a2.5 2.5 0 0 0 3.5 3.5L9.5 12M8.5 6 10 4.5A2.5 2.5 0 0 1 13.5 8L12 9.5",
        ActionIconKind.Bank => "M2.5 7.5 9 3.5l6.5 4M4 8v5.5M7.3 8v5.5M10.7 8v5.5M14 8v5.5M2.5 15.5h13",
        _ => "M11.5 3.5l3 3L6 15H3v-3z",
    };

    public ActionIcon(ActionIconKind kind, Color? ink = null)
    {
        Drawable = new Painter(PathOf(kind), ink ?? PaperResources.Colour("Ink"));
        WidthRequest = SizeDp;
        HeightRequest = SizeDp;
        VerticalOptions = LayoutOptions.Start;
    }

    private sealed class Painter(string pathData, Color ink) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.Antialias = true;
            canvas.StrokeColor = ink;
            canvas.StrokeSize = StrokeDp;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.DrawPath(PathBuilder.Build(pathData));
        }
    }
}
