using SkiaSharp;
using Foreword.Domain.Document;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

/// <summary>
/// What every page of The Day has in common (D1-D4): the shared frame, plus this document's own
/// four tabs and their cloth.
///
/// <para>One place, because four pages that each draw their own header is four places for them to
/// drift apart — and because the tabs only work if every page draws all four of them and anchors
/// itself. A page that forgets its anchor is a page three tabs cannot reach.</para>
///
/// <para>The frame itself lives in <see cref="PageFurniture"/>, shared with the prayer document,
/// which is meant to read as part of the same morning rather than as a different app's output.</para>
/// </summary>
internal static class DayPageFurniture
{
    /// <inheritdoc cref="PageFurniture.TabGutter"/>
    public const double TabGutter = PageFurniture.TabGutter;

    /// <summary>The right edge of a tabbed page's content, in page points.</summary>
    public static double ContentRight(SkiaPageLayout layout) => PageFurniture.ContentRight(layout);

    public static double ContentWidth(SkiaPageLayout layout) => PageFurniture.ContentWidth(layout);

    /// <summary>
    /// Draws the header, the rule and the tab strip, anchors the page, and leaves the cursor under
    /// the rule ready for the page's own content.
    /// </summary>
    public static void Draw(SkiaPageLayout layout, DaySection section, string title)
    {
        PageFurniture.DrawHeader(layout, title);
        DrawTabs(layout, section);
    }

    /// <summary>
    /// The binder strip. Every page draws all four tabs and anchors itself, so the strip is the same
    /// on every sheet and the three it is not on are a tap away.
    /// </summary>
    private static void DrawTabs(SkiaPageLayout layout, DaySection active)
    {
        // The page's own name, so the other three pages' links land here.
        layout.Anchor(SectionTabs.Destination(active));

        PageFurniture.DrawBinderEdge(layout, SectionTabs.BinderEdgeInset, SectionTabs.Hairline);

        foreach (var tab in SectionTabs.Layout((float)layout.PageWidth, active))
        {
            PageFurniture.DrawTab(
                layout, tab.Left, tab.Top, tab.Width, tab.Height, tab.Active,
                SectionTabs.Label(tab.Section),
                SectionTabs.Destination(tab.Section),
                Cloth(tab.Section),
                Ink(tab.Section),
                SectionTabs.Radius,
                SectionTabs.Hairline);
        }
    }

    /// <summary>
    /// The cloth an inactive tab is cut from (§9: tabs are colour-coded per area). Day takes the
    /// identity, because it is the one section that is the app itself rather than a kind of content
    /// — which is also why it cannot be a role: a role says what something means.
    /// </summary>
    private static SKColor Cloth(DaySection section) => section switch
    {
        DaySection.Day => AppIdentityTokens.ForewordGround,
        _ => PaperPalette.ResolveRole(Role(section), ColorTier.Fill, UiColorProfile.DocumentColor),
    };

    /// <summary>The ink an active tab's label takes — its own hue, on the page ground.</summary>
    private static SKColor Ink(DaySection section) => section switch
    {
        DaySection.Day => AppIdentityTokens.ForewordDeep,
        _ => PaperPalette.ResolveRole(Role(section), ColorTier.Text, UiColorProfile.DocumentColor),
    };

    private static ColorRole Role(DaySection section) => section switch
    {
        DaySection.Weather => ColorRole.InProgress,
        DaySection.Outlook => ColorRole.Reference,
        _ => ColorRole.Action,
    };
}
