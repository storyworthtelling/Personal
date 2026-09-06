using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Storyworthtelling.Maui.Layout;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// One area the binder can turn to.
///
/// <para>It carries TWO colours because a tab is coloured differently depending on which side of
/// the seam it is on (DESIGN.md §9): <paramref name="Cloth"/> is the area's fill tier, the coloured
/// cloth an INACTIVE tab is cut from, and <paramref name="Hue"/> is the mark tier the ACTIVE tab
/// sets its glyph and label in once the tab itself has gone page-coloured.</para>
/// </summary>
public sealed record BinderArea(
    string Label, ImageSource Glyph, ImageSource GlyphActive, Color Hue, Color Cloth);

/// <summary>
/// In-app navigation as the right-edge divider tabs of a school notebook (DESIGN.md §9), for the
/// MAUI heads — the counterpart of <c>Codex.Ui.Android</c>'s <c>BinderTabs</c>, and deliberately the
/// same shape as it: a glyph over a horizontal label filling the gutter, so the strip is one
/// component across all five apps rather than one per UI stack.
///
/// <para>A <b>page-edge line</b> runs down the left of the gutter; inactive tabs sit outside it in
/// their own area's cloth; the <b>active tab is page-coloured</b>, so it blends into the content at
/// the seam instead of crossing into it, and starts on the line so the seam closes.</para>
///
/// <para>Positions come from the pure <see cref="BinderTabLayout"/> — the same file the native
/// component uses, kept identical by a parity guard — so the rule that every tab clears §5's 48dp
/// minimum, and that the strip scrolls rather than shrinking past it, is decided and tested away
/// from the device. This view is a thin painter.</para>
///
/// <para>Tapping the tab you are already on is how you return that area to its root, so the
/// callback fires for the active index too rather than being swallowed.</para>
/// </summary>
public class BinderTabs : ContentView
{
    /// <summary>The reserved gutter. Wide enough for the longest area label.</summary>
    public const int WidthDp = 66;

    private readonly AbsoluteLayout strip = new();
    private readonly List<BinderArea> areas = [];
    private int activeIndex;

    public BinderTabs()
    {
        WidthRequest = WidthDp;
        Content = strip;
        SizeChanged += (_, _) => Rebuild();
    }

    /// <summary>Fires with the tapped index — including the active one.</summary>
    public event Action<int>? AreaSelected;

    public void SetAreas(IEnumerable<BinderArea> value, int active)
    {
        areas.Clear();
        areas.AddRange(value);
        activeIndex = active;
        Rebuild();
    }

    public void SetActive(int index)
    {
        activeIndex = index;
        Rebuild();
    }

    private void Rebuild()
    {
        strip.Children.Clear();
        if (Height <= 0)
        {
            // Laid out on the first SizeChanged; the well's height is what decides tab heights,
            // so there is nothing meaningful to draw before it is known.
            return;
        }

        // The page edge: the line the tabs are clipped at, running the full height.
        var edge = new BoxView { Color = PaperResources.Colour("Hairline") };
        AbsoluteLayout.SetLayoutBounds(edge, new Rect(0, 0, DesignTokens.Shape.HairlineDp, Height));
        strip.Children.Add(edge);

        var tabs = BinderTabLayout.Compute(areas.Count, (int)Height);
        for (var i = 0; i < areas.Count; i++)
        {
            var view = BuildTab(areas[i], i);

            // An inactive tab sits a hairline outside the page edge and points away from the page;
            // the active one starts on the line so the seam closes.
            var left = i == activeIndex ? 0 : DesignTokens.Shape.HairlineDp;
            AbsoluteLayout.SetLayoutBounds(
                view, new Rect(left, tabs[i].TopDp, WidthDp - left, tabs[i].HeightDp));
            strip.Children.Add(view);
        }
    }

    private View BuildTab(BinderArea area, int index)
    {
        var active = index == activeIndex;

        // On the page-coloured active tab the hue is the only thing marking the area, so it goes in
        // the ink. On the coloured cloth of an inactive tab the cloth is already saying it, and
        // colouring the label too would leave a strip that reads as a row of disabled buttons.
        var tint = active ? area.Hue : PaperResources.Colour("Ink");

        // TWO sources rather than one tinted at runtime. MAUI has no equivalent of Android's
        // ImageView.SetColorFilter, and the only ready answer is a community toolkit behaviour —
        // a dependency this repository would not take for one tint. So the head supplies the
        // glyph already inked both ways; IconStudio generates per-hue assets already.
        var glyph = new Image
        {
            Source = active ? area.GlyphActive : area.Glyph,
            WidthRequest = 26,
            HeightRequest = 26,
        };

        var label = new Label
        {
            Text = area.Label,
            Style = PaperResources.Token<Style>("FieldLabel"),
            TextColor = tint,
            LineBreakMode = LineBreakMode.NoWrap,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, DesignTokens.Space.X1, 0, 0),
        };

        var column = new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { glyph, label },
        };

        var corners = BinderTabCorners.Outer(DesignTokens.Shape.RadiusCard);
        var tab = new Border
        {
            Content = column,
            BackgroundColor = active ? PaperResources.Colour("Paper") : area.Cloth,
            Stroke = PaperResources.Colour("Hairline"),
            StrokeThickness = DesignTokens.Shape.HairlineDp,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(
                    corners.TopLeft, corners.TopRight, corners.BottomLeft, corners.BottomRight),
            },
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => AreaSelected?.Invoke(index);
        tab.GestureRecognizers.Add(tap);
        return tab;
    }

}
