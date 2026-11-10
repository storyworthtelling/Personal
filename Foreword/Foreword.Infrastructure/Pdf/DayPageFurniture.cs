using SkiaSharp;
using Foreword.Domain.Document;
using Storyworthtelling.Paper;
using Storyworthtelling.Paper.Binder;

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
    /// Draws the header, the rule and the tab strip, anchors the page, sets its folio, and leaves the cursor under the
    /// rule ready for the page's own content.
    /// </summary>
    public static void Draw(SkiaPageLayout layout, DaySection section, string title)
    {
        PageFurniture.DrawHeader(layout, title);
        PageFurniture.DrawBinder(layout, Tabs, (int)section, SectionTabs.FirstTabTop);
        layout.Footer = PageFurniture.Folio(
            section == DaySection.Day ? "The Day · today" : $"The Day · {SectionTabs.Label(section).ToLowerInvariant()}",
            ((int)section + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>All four tabs, in bound order: every page draws the whole strip, so the three it is not on are a tap away.</summary>
    private static readonly IReadOnlyList<BinderTab> Tabs = Enum.GetValues<DaySection>()
        .Select(section => new BinderTab(SectionTabs.Label(section), SectionTabs.Destination(section), Cloth(section), Ink(section)))
        .ToList();

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
