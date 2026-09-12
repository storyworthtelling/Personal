using Microsoft.Maui.Graphics;
using Storyworthtelling.Maui.Layout;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// Paints <see cref="MarkerRule"/>'s two strokes in one colour. The geometry decides, this only
/// draws — the same split the dog-ear uses, and the reason the rule can be tested at all.
/// </summary>
public sealed class MarkerRuleDrawable : IDrawable
{
    /// <summary>The ink. An area's identity hue, or the neutral grey on Settings (DESIGN.md §9).</summary>
    public Color Ink { get; set; } = Colors.Black;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
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
