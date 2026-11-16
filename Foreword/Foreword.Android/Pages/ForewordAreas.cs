using Storyworthtelling.Materials;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;
using Storyworthtelling.Paper;

namespace Foreword.Android.Pages;

/// <summary>Which of the four areas a screen belongs to. The order is the strip's order.</summary>
public enum ForewordArea
{
    Today,
    Tasks,
    Calendar,
    Settings,
}

/// <summary>
/// Foreword's four binder areas: the label, the glyph, and the two colours §9 gives a tab.
///
/// <para>Home takes the <b>identity</b> hue and Settings is always the neutral grey; the two in
/// between take a role each, far enough apart on the wheel to be told apart at a glance. The glyphs
/// are the suite's one set, <c>Storyworthtelling.Materials.TabGlyphs</c>, drawn in colored pencil by the
/// kit's strip (DESIGN.md §10, W8.8).</para>
///
/// <para>This exists because Foreword shipped with no strip at all: the kit's <c>BinderTabs</c> was
/// built and then never wired to a page, and the app navigated by two quiet buttons in the bottom
/// bar instead — which put Calendar three taps deep inside Settings, where the mockups have it as
/// one of the four things the app is.</para>
/// </summary>
public static class ForewordAreas
{
    /// <summary>The app's identity hue, as a MAUI colour. The token is the printed page's.</summary>
    public static Color Identity { get; } = Color.FromArgb(AppIdentityTokens.ForewordSunriseHex);

    /// <summary>The cloth an inactive Today tab is cut from.</summary>
    public static Color IdentityCloth { get; } = Color.FromArgb(AppIdentityTokens.ForewordFillHex);

    /// <summary>
    /// The rule under a screen's header: its area's colour, and the neutral grey on Settings — a
    /// settings screen is grey in every app in the suite, so it never looks like somewhere you are.
    /// </summary>
    public static Color RuleInk(ForewordArea area) =>
        area == ForewordArea.Settings ? PaperResources.Colour("ResolvedMark") : All[(int)area].Hue;

    public static IReadOnlyList<BinderArea> All { get; } =
    [
        new("Today", TabGlyphs.Sunrise, Identity, IdentityCloth),
        new("Tasks", TabGlyphs.Checklist,
            PaperResources.Colour("ActionMark"), PaperResources.Colour("ActionFill")),
        new("Calendar", TabGlyphs.Calendar,
            PaperResources.Colour("ReferenceMark"), PaperResources.Colour("ReferenceFill")),
        new("Settings", TabGlyphs.Settings,
            PaperResources.Colour("ResolvedMark"), PaperResources.Colour("ResolvedFill")),
    ];
}
