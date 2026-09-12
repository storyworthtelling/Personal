using SkiaSharp;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

/// <summary>
/// The frame every delivered sheet shares: the header, the identity rule, and the binder grammar.
///
/// <para>Two documents carry it now — The Day's four tabs and Prayer's two — and the prayer
/// document is meant to read as part of the same morning rather than as a different app's output.
/// That only holds if the frame is one piece of code. Two copies drift, and the way they drift is
/// exactly the thing the owner would notice and could not name: the rule a point thicker, the brand
/// four points higher, the tab seam closing on the wrong side.</para>
/// </summary>
internal static class PageFurniture
{
    /// <summary>
    /// The right margin on a tabbed page. Wider than the left because the tab strip lives out here:
    /// the content stops clear of the binder edge rather than running under it.
    /// </summary>
    public const double TabGutter = 38;

    public const double HeaderHeight = 22;
    public const double RuleHeight = 2;
    public const double RuleGap = 5;

    // Foreword signs its own pages. This is the identity hue, not a role — see AppIdentityTokens.
    public static readonly SKColor Identity = AppIdentityTokens.ForewordSunrise;

    /// <summary>The right edge of a tabbed page's content, in page points.</summary>
    public static double ContentRight(SkiaPageLayout layout) => layout.PageWidth - TabGutter;

    public static double ContentWidth(SkiaPageLayout layout) =>
        ContentRight(layout) - SkiaPageLayout.Margin;

    /// <summary>
    /// The header and the rule, leaving the cursor under the rule ready for the page's own content.
    /// </summary>
    public static void DrawHeader(SkiaPageLayout layout, string title)
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
    }

    /// <summary>The hairline the inactive tabs sit behind, down the full height of the sheet.</summary>
    public static void DrawBinderEdge(SkiaPageLayout layout, float inset, float hairline)
    {
        var edgeX = layout.PageWidth - inset;
        layout.DrawLine(edgeX, 0, edgeX, layout.PageHeight, SkiaPageLayout.Hairline, hairline);
    }

    /// <summary>
    /// One tab: the shape, the label running down it, and the link.
    ///
    /// <para>The whole tab is the target, including the one it is already on. A link to the page you
    /// are reading is harmless, and leaving it out makes one tab inert for no reason a finger could
    /// predict.</para>
    /// </summary>
    public static void DrawTab(
        SkiaPageLayout layout,
        double left, double top, double width, double height,
        bool active, string label, string destination,
        SKColor cloth, SKColor activeInk, float radius, float hairline)
    {
        // An inactive tab is cut from its own area's cloth; the active one is page-coloured and
        // carries the hue in its ink instead, so it reads as the open divider.
        layout.DrawTabRect(left, top, width, height, radius,
            roundRight: true,
            fill: active ? SkiaPageLayout.Ground : cloth,
            stroke: SkiaPageLayout.Hairline,
            strokeWidth: hairline);

        DrawVerticalLabel(layout, left, top, width, height, label,
            active ? activeInk : SkiaPageLayout.Ink);

        layout.LinkTo(left, top, width, height, destination);
    }

    /// <summary>
    /// The label runs down the tab, because a tab is 24 points wide and "Checklist" is not. Rotated
    /// rather than set in a narrow column: stacked letters are not a word, they are a puzzle.
    /// </summary>
    private static void DrawVerticalLabel(
        SkiaPageLayout layout,
        double left, double top, double width, double height,
        string text, SKColor colour)
    {
        var font = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);
        var textWidth = layout.MeasureText(text, font);

        layout.Rotated(
            left + width / 2,
            top + (height - textWidth) / 2,
            degrees: 90,
            () => layout.DrawText(text, font, 0, 0, height, width, TextAlign.TopLeft, colour));
    }
}
