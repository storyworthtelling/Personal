using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Platform;
using Storyworthtelling.Materials.Android;
using Storyworthtelling.Maui.Layout;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// Paints <see cref="MarkerRule"/>'s two strokes in one colour. The geometry decides, this only
/// draws — the same split the dog-ear uses, and the reason the rule can be tested at all.
/// </summary>
public sealed class MarkerRuleDrawable : IDrawable
{
    /// <summary>The ink. An area's identity hue, or the neutral grey on Settings (DESIGN.md §9).</summary>
    public Color Ink { get; set; } = PaperResources.Colour("Ink");

    // §2 Marker: the strokes carry fibre streaks along their length, as the native underline does.
    private readonly global::Android.Graphics.Paint marker = new();

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (canvas is PlatformCanvas platform && platform.Canvas is { } native)
        {
            foreach (var design in MarkerRule.Strokes)
            {
                var stroke = MarkerRule.Scale(design, dirtyRect.Width, dirtyRect.Height);
                var path = new global::Android.Graphics.Path();
                path.MoveTo(dirtyRect.Left + stroke.StartX, dirtyRect.Top + stroke.StartY);
                path.CubicTo(
                    dirtyRect.Left + stroke.Control1X, dirtyRect.Top + stroke.Control1Y,
                    dirtyRect.Left + stroke.Control2X, dirtyRect.Top + stroke.Control2Y,
                    dirtyRect.Left + stroke.EndX, dirtyRect.Top + stroke.EndY);

                // The GraphicsView's canvas is in dp, so one dp is one canvas unit.
                MaterialPainters.Marker(marker, Ink.WithAlpha(stroke.Opacity).ToPlatform(), 1f);
                marker.SetStyle(global::Android.Graphics.Paint.Style.Stroke);
                marker.StrokeWidth = stroke.Width;
                marker.StrokeCap = global::Android.Graphics.Paint.Cap.Round;
                native.DrawPath(path, marker);
            }
            return;
        }

        canvas.Antialias = true;
        canvas.StrokeLineCap = LineCap.Round;

        foreach (var design in MarkerRule.Strokes)
        {
            var stroke = MarkerRule.Scale(design, dirtyRect.Width, dirtyRect.Height);

            var path = new PathF();
            path.MoveTo(stroke.StartX, stroke.StartY);
            path.CurveTo(
                stroke.Control1X, stroke.Control1Y,
                stroke.Control2X, stroke.Control2Y,
                stroke.EndX, stroke.EndY);

            canvas.StrokeColor = Ink.WithAlpha(stroke.Opacity);
            canvas.StrokeSize = stroke.Width;
            canvas.DrawPath(path);
        }
    }
}

/// <summary>
/// A screen's in-page header (DESIGN.md §5): the screen's name in Serif <b>Black</b>, with a
/// hand-drawn rule under it in the area's colour.
///
/// <para>It is one component rather than a label each page draws for itself, because Foreword's
/// screens proved what happens otherwise: every page had a Display label, none had a rule, and the
/// app read as a settings list wearing a serif font. The rule is not decoration — it is the thing
/// that says which area you are in, and it is the screen-side half of the same identity the binder
/// tab carries.</para>
///
/// <para>Left-aligned, never centred. A centred title reads as a dialog; this is a page.</para>
/// </summary>
public sealed class PaperPageHeader : ContentView
{
    private readonly Label title = new()
    {
        Style = PaperResources.Token<Style>("DisplayLabel"),
        HorizontalOptions = LayoutOptions.Start,
    };

    private readonly MarkerRuleDrawable rule = new();
    private readonly GraphicsView ruleView;

    public PaperPageHeader()
    {
        ruleView = new GraphicsView
        {
            Drawable = rule,
            HeightRequest = MarkerRule.HeightDp,
            Margin = new Thickness(0, MarkerRule.TopGapDp, 0, 0),
        };

        // The header owns its own spacing so every screen sits its title at the same height; a page
        // supplies padding for its CONTENT, not for this.
        Padding = new Thickness(
            DesignTokens.Space.X6, DesignTokens.Space.X8, DesignTokens.Space.X6, DesignTokens.Space.X2);

        Content = new VerticalStackLayout { Children = { title, ruleView } };
    }

    public string Text
    {
        get => title.Text ?? string.Empty;
        set => title.Text = value;
    }

    /// <summary>The rule's ink. Settings passes the neutral grey; every other area its own hue.</summary>
    public Color Ink
    {
        get => rule.Ink;
        set
        {
            rule.Ink = value;
            ruleView.Invalidate();
        }
    }
}
