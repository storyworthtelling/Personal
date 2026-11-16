using Storyworthtelling.Materials;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;
using Storyworthtelling.Paper;

namespace Folio.Android.Pages;

/// <summary>Which of the three areas a screen belongs to. The order is the strip's order.</summary>
public enum FolioArea
{
    Work,
    Shelf,
    Settings,
}

/// <summary>
/// Folio's three binder areas: the label, the glyph, and the two colours §9 gives a tab.
///
/// <para><b>Work is home</b> and takes the identity olive; Shelf takes Reference, the role for
/// structure and what has been put away; Settings is the neutral grey it is in every app in the
/// suite, so it never looks like somewhere you are. Three areas, not four — this app does one thing,
/// and a tab per screen would be a menu wearing a binder's clothes.</para>
///
/// <para>The glyphs are the suite's one set, <c>Storyworthtelling.Materials.TabGlyphs</c>, drawn in colored
/// pencil by the kit's strip (DESIGN.md §10, W8.8).</para>
/// </summary>
public static class FolioAreas
{
    /// <summary>The app's identity hue, as a MAUI colour. The token is the printed page's.</summary>
    public static Color Identity { get; } = Color.FromArgb(AppIdentityTokens.FolioMarkHex);

    /// <summary>The cloth an inactive Work tab is cut from.</summary>
    public static Color IdentityCloth { get; } = Color.FromArgb(AppIdentityTokens.FolioFillHex);

    /// <summary>
    /// The rule under a screen's header: its area's colour, and the neutral grey on Settings.
    /// </summary>
    public static Color RuleInk(FolioArea area) =>
        area == FolioArea.Settings ? PaperResources.Colour("ResolvedMark") : All[(int)area].Hue;

    public static IReadOnlyList<BinderArea> All { get; } =
    [
        new("Work", TabGlyphs.OpenBook, Identity, IdentityCloth),
        new("Shelf", TabGlyphs.BookShelf,
            PaperResources.Colour("ReferenceMark"), PaperResources.Colour("ReferenceFill")),
        new("Settings", TabGlyphs.Settings,
            PaperResources.Colour("ResolvedMark"), PaperResources.Colour("ResolvedFill")),
    ];
}
