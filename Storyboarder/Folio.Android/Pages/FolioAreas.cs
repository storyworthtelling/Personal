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
/// <para>The glyphs are 24-unit <b>fill</b> path data, the kit's convention, because
/// <c>BinderGlyphDrawable</c> fills them in two different inks — ink on an inactive tab's coloured
/// cloth, the area's own hue on the page-coloured active one — and an <c>ImageSource</c> cannot be
/// re-tinted: MAUI has no equivalent of Android's <c>SetColorFilter</c>.</para>
/// </summary>
public static class FolioAreas
{
    // An open book, seen from above: two leaves falling away from a centre gap that is the spine.
    // The gap is the glyph — two filled quadrilaterals would read as a shape, not a book.
    private const string WorkGlyph =
        "M2.6 5.4C5 4.6 8.4 4.4 11 5.2V19.6C8.4 18.8 5 19 2.6 19.8ZM21.4 5.4C19 4.6 15.6 4.4 13 "
        + "5.2V19.6C15.6 18.8 19 19 21.4 19.8Z";

    // Spines standing on a shelf, two of them leaning — which is what tells it from a bar chart.
    private const string ShelfGlyph =
        "M2.8 19.7h18.4v1.9H2.8zM4.2 5.2h2.5v14.5H4.2zM8 4.6h2.4v15.1H8zM12.4 19.7 14.6 5.4l2.3.5"
        + "-2.2 13.8ZM15.6 19.7 18.4 6.2l2.2.6-2.7 12.9Z";

    // Three sliders, and deliberately not the cog the mockup drew. Foreword settled this for the
    // suite — the suite's settings are choices, not machinery — and a divider that is one component
    // across five apps cannot have a different glyph in the sixth.
    private const string SettingsGlyph =
        "M3 5.2h8v1.8H3zm11.5 0H21v1.8h-6.5zM12 2.6h1.8v7H12zM3 11.1h4v1.8H3zm7.5 0H21v1.8H10.5zM8 "
        + "8.5h1.8v7H8zM3 17h10v1.8H3zm12.5 0H21v1.8h-5.5zM13 14.4h1.8v7H13z";

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
        new("Work", WorkGlyph, Identity, IdentityCloth),
        new("Shelf", ShelfGlyph,
            PaperResources.Colour("ReferenceMark"), PaperResources.Colour("ReferenceFill")),
        new("Settings", SettingsGlyph,
            PaperResources.Colour("ResolvedMark"), PaperResources.Colour("ResolvedFill")),
    ];
}
