using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
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
///
/// <para><paramref name="GlyphPath"/> is SVG path data in a <b>24-unit</b> viewport, the suite's
/// convention for a glyph. It is path data rather than an image because the same glyph is drawn in
/// two inks — ink on the coloured cloth of an inactive tab, the area's hue on the page-coloured
/// active one — and an <c>ImageSource</c> cannot be re-tinted: MAUI has no equivalent of Android's
/// <c>SetColorFilter</c>. The alternative was shipping every glyph twice, already inked both ways,
/// which is two assets per area that can drift apart and a palette baked into a resource file where
/// the leak guard cannot see it.</para>
/// </summary>
public sealed record BinderArea(string Label, string GlyphPath, Color Hue, Color Cloth);

/// <summary>
/// A glyph from 24-unit path data, filled in one colour and fitted to the box it is given.
/// </summary>
public sealed class BinderGlyphDrawable : IDrawable
{
    /// <summary>The viewport glyph path data is written in.</summary>
    public const float Viewport = 24f;

    public string? PathData { get; set; }

    public Color Ink { get; set; } = Colors.Black;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (string.IsNullOrWhiteSpace(PathData))
        {
            return;
        }

        canvas.Antialias = true;
        canvas.SaveState();
        canvas.Scale(dirtyRect.Width / Viewport, dirtyRect.Height / Viewport);
        canvas.FillColor = Ink;
        canvas.FillPath(PathBuilder.Build(PathData), WindingMode.NonZero);
        canvas.RestoreState();
    }
}

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

    /// <summary>The glyph over the label, at the size the mockups draw it.</summary>
    public const int GlyphDp = 24;

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

        // Fills the whole gutter with the active area's cloth, as Codex.Ui.Android's BinderTabs
        // (UpdateGutterColor) already does natively (DESIGN.md §9, D121) — otherwise the gaps
        // between and around the tabs fall through to whatever the page behind this view is,
        // instead of reading as the same cloth the tabs around them wear. Set ahead of the early
        // return below so it holds even before the strip has a height to lay tabs out in, and
        // before there are any areas at all.
        strip.BackgroundColor = activeIndex >= 0 && activeIndex < areas.Count
            ? areas[activeIndex].Cloth
            : PaperResources.Colour("Paper");

        if (areas.Count == 0)
        {
            return;
        }

        // SetAreas is called from a page's constructor, before Wrap has even added this view to the
        // page — so the first Rebuild() always runs detached, with no height yet. Bailing out there
        // (as this used to) left the whole gutter blank — no page edge, no tab cloth, no colour — for
        // however long it took the platform to run this view's first layout pass and raise
        // SizeChanged, which on a cold Android launch is a real, visible gap: Plate, Score and Post
        // never show it because their native BinderTabs computes its layout synchronously inside
        // onLayout, with no such window. Painting at the preferred per-tab height as a placeholder
        // means the strip is never blank; this reruns and snaps to the true geometry once Height is
        // known (observed live on device for Folio and Foreword, both built on this component).
        var wellHeightDp = Height > 0
            ? (int)Height
            : BinderTabLayout.PreferredHeightDp * (areas.Count + 1);

        // The page edge: the line the tabs are clipped at, running the full height.
        var edge = new BoxView { Color = PaperResources.Colour("Hairline") };
        AbsoluteLayout.SetLayoutBounds(edge, new Rect(0, 0, DesignTokens.Shape.HairlineDp, wellHeightDp));
        strip.Children.Add(edge);

        var tabs = BinderTabLayout.Compute(areas.Count, wellHeightDp);
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

        // Drawn rather than loaded, so one glyph serves both inks. See BinderArea.
        var glyph = new GraphicsView
        {
            Drawable = new BinderGlyphDrawable { PathData = area.GlyphPath, Ink = tint },
            WidthRequest = GlyphDp,
            HeightRequest = GlyphDp,
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
