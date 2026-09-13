using SkiaSharp;
using Storyworthtelling.Paper;

namespace Folio.Infrastructure.Imaging;

/// <summary>
/// Every colour the report prints in, resolved once, here.
///
/// <para><b>Why the Document profile.</b> DESIGN.md §3 gives a role two palettes: the lively five-tier
/// App set for a screen, and a muted set for a document, because a hue that reads as a control on a
/// backlit panel reads as a highlighter on a sheet. The report is a document. Until this release it
/// drew its accent from <c>PaperPalette.Resolve(PaperRole.Active, …)</c>, which is the older
/// device-adaptive resolver and still hands back the <b>retired terracotta</b> <c>#C2542F</c> — a
/// colour the bible replaced and nothing in the app uses. That is the one thing this file exists to
/// make impossible: there is no route from a report page to a role colour that is not the Document
/// one.</para>
///
/// <para>Ground, ink and hairline are not roles — they are the sheet and what is drawn on it — so
/// they still come from the paper resolver, where the Screen ground is the report's target (the
/// Drive PDF and the in-app read). The rM2 gray ramp is the same call with
/// <see cref="UiColorProfile.DocumentGray"/> when per-destination rendering is wired.</para>
/// </summary>
public static class ReportPalette
{
    private const UiColorProfile Profile = UiColorProfile.DocumentColor;
    private const DeviceColorProfile Device = DeviceColorProfile.Screen;

    public static readonly SKColor Ground = PaperPalette.Resolve(PaperRole.Ground, Device);
    public static readonly SKColor Ink = PaperPalette.Resolve(PaperRole.Ink, Device);
    public static readonly SKColor InkSoft = PaperPalette.Resolve(PaperRole.InkSoft, Device);
    public static readonly SKColor InkMuted = PaperPalette.Resolve(PaperRole.InkMuted, Device);
    public static readonly SKColor Hairline = PaperPalette.Resolve(PaperRole.Hairline, Device);

    /// <summary>The rule under a page's label, and the mark on a citation: the muted pine.</summary>
    public static readonly SKColor Rule = Role(ColorRole.Action);

    /// <summary>The four severities the marks and the key are drawn in (DESIGN.md §9).</summary>
    public static readonly SKColor MustFix = Role(ColorRole.Problem);
    public static readonly SKColor Should = Role(ColorRole.Caution);
    public static readonly SKColor Consider = Role(ColorRole.Reference);
    public static readonly SKColor Great = Role(ColorRole.Positive);

    private static SKColor Role(ColorRole role) =>
        PaperPalette.ResolveRole(role, ColorTier.Mark, Profile);
}
