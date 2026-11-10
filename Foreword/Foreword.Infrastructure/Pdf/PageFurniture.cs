using SkiaSharp;
using Storyworthtelling.Paper;
using Storyworthtelling.Paper.Binder;

namespace Foreword.Infrastructure.Pdf;

/// <summary>
/// Foreword's side of the frame every tablet document shares (<see cref="SheetFrame"/>): its signature, and the
/// header, binder strip and folio drawn on a <see cref="SkiaPageLayout"/>. The frame was Foreword's own and is now
/// the suite's, by the owner's decision, so The Day, Prayer and the brief draw through the same code Index,
/// Constitution and Folio will.
/// </summary>
internal static class PageFurniture
{
    /// <summary>The right margin on a tabbed page: the content stops clear of the binder edge.</summary>
    public const double TabGutter = BinderStrip.Gutter;

    public const double HeaderHeight = SheetFrame.HeaderHeight;

    // Foreword signs its own pages. This is the identity hue, not a role — see AppIdentityTokens.
    public static readonly SKColor Identity = AppIdentityTokens.ForewordSunrise;

    public static readonly SheetSignature Signature =
        new("Foreword", AppIdentityTokens.ForewordSunrise, AppIdentityTokens.ForewordGround, AppIdentityTokens.ForewordDeep);

    /// <summary>The right edge of a tabbed page's content, in page points.</summary>
    public static double ContentRight(SkiaPageLayout layout) => BinderStrip.ContentRight((float)layout.PageWidth);

    public static double ContentWidth(SkiaPageLayout layout) => ContentRight(layout) - SkiaPageLayout.Margin;

    /// <summary>The header and the rule, leaving the cursor under the rule ready for the page's own content.</summary>
    public static void DrawHeader(SkiaPageLayout layout, string title) =>
        layout.MoveTo(SheetFrame.Header(layout.Canvas, layout.Sheet, title, Signature));

    /// <summary>The binder strip: every tab on every page, this one anchored.</summary>
    public static void DrawBinder(SkiaPageLayout layout, IReadOnlyList<BinderTab> tabs, int active, float firstTabTop) =>
        SheetFrame.Binder(layout.Canvas, layout.Sheet, tabs, active, firstTabTop);

    /// <summary>A footer that draws the shared folio — the maker's mark, the page's name and the words on the right.</summary>
    public static Action<SkiaPageLayout> Folio(string name, string? right) =>
        page => SheetFrame.Folio(page.Canvas, page.Sheet, name, right);
}
