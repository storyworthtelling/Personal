using SkiaSharp;
using Foreword.Domain.Document;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

/// <summary>
/// What every page of The Day has in common: the header, the identity rule, and the binder tabs
/// down the right edge (D1-D4).
///
/// <para>One place, because four pages that each draw their own header is four places for them to
/// drift apart — and because the tabs only work if every page draws all four of them and anchors
/// itself. A page that forgets its anchor is a page three tabs cannot reach.</para>
/// </summary>
internal static class DayPageFurniture
{
    /// <summary>
    /// The right margin on a tabbed page. Wider than the left because the tab strip lives out here:
    /// the content stops clear of the binder edge rather than running under it.
    /// </summary>
    public const double TabGutter = 38;

    private const double HeaderHeight = 22;
    private const double RuleHeight = 2;
    private const double RuleGap = 5;

    // Foreword signs its own pages. This is the identity hue, not a role — see AppIdentityTokens.
    private static readonly SKColor Identity = AppIdentityTokens.ForewordSunrise;

    /// <summary>The right edge of a tabbed page's content, in page points.</summary>
    public static double ContentRight(SkiaPageLayout layout) => layout.PageWidth - TabGutter;

    public static double ContentWidth(SkiaPageLayout layout) =>
        ContentRight(layout) - SkiaPageLayout.Margin;

    /// <summary>
    /// Draws the header, the rule and the tab strip, anchors the page, and leaves the cursor under
    /// the rule ready for the page's own content.
    /// </summary>
    public static void Draw(SkiaPageLayout layout, DaySection section, string title)
    {
        var titleFont = SkiaPageLayout.Serif(PaperTypeScale.Title);
        var brandFont = SkiaPageLayout.Script(PaperTypeScale.Title);

        layout.MoveTo(SkiaPageLayout.Margin);

        layout.DrawText(title, titleFont, SkiaPageLayout.Margin, layout.Y,
            ContentWidth(layout), HeaderHeight, TextAlign.TopLeft);

        // The app's name in the owner's hand — the one place on a document where the app signs it.
        layout.DrawText("Foreword", brandFont, SkiaPageLayout.Margin, layout.Y,
            ContentWidth(layout), HeaderHeight, TextAlign.TopRight, Identity);

        layout.Advance(HeaderHeight + RuleGap);
        layout.DrawRect(SkiaPageLayout.Margin, layout.Y, ContentWidth(layout), RuleHeight,
            fill: Identity);
        layout.Advance(RuleHeight + RuleGap);

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

        var edgeX = layout.PageWidth - SectionTabs.BinderEdgeInset;
        layout.DrawLine(edgeX, 0, edgeX, layout.PageHeight, SkiaPageLayout.Hairline, SectionTabs.Hairline);

        var labelFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);

        foreach (var tab in SectionTabs.Layout((float)layout.PageWidth, active))
        {
            // An inactive tab is cut from its own area's cloth; the active one is page-coloured and
            // carries the hue in its ink instead, so it reads as the open divider.
            layout.DrawTabRect(tab.Left, tab.Top, tab.Width, tab.Height,
                SectionTabs.Radius,
                roundRight: true,
                fill: tab.Active ? SkiaPageLayout.Ground : Cloth(tab.Section),
                stroke: SkiaPageLayout.Hairline,
                strokeWidth: SectionTabs.Hairline);

            DrawVerticalLabel(layout, tab, labelFont, tab.Active ? Ink(tab.Section) : SkiaPageLayout.Ink);

            // The whole tab is the target, including the one it is already on — a link to the page
            // you are reading is harmless, and leaving it out would make one tab in four inert for
            // no reason a finger could predict.
            layout.LinkTo(tab.Left, tab.Top, tab.Width, tab.Height,
                SectionTabs.Destination(tab.Section));
        }
    }

    /// <summary>
    /// The label runs down the tab, because a tab is 24 points wide and "Checklist" is not. Rotated
    /// rather than set in a narrow column: stacked letters are not a word, they are a puzzle.
    /// </summary>
    private static void DrawVerticalLabel(
        SkiaPageLayout layout, TabBox tab, SKFont font, SKColor colour)
    {
        var text = SectionTabs.Label(tab.Section);
        var width = layout.MeasureText(text, font);

        layout.Rotated(
            tab.Left + tab.Width / 2,
            tab.Top + (tab.Height - width) / 2,
            degrees: 90,
            () => layout.DrawText(text, font, 0, 0, tab.Height, tab.Width, TextAlign.TopLeft, colour));
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
