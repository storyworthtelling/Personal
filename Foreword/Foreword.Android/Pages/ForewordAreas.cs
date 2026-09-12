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
/// are the mockups' own path data, in the suite's 24-unit viewport, so the strip on the phone is the
/// strip that was drawn rather than an interpretation of it.</para>
///
/// <para>This exists because Foreword shipped with no strip at all: the kit's <c>BinderTabs</c> was
/// built and then never wired to a page, and the app navigated by two quiet buttons in the bottom
/// bar instead — which put Calendar three taps deep inside Settings, where the mockups have it as
/// one of the four things the app is.</para>
/// </summary>
public static class ForewordAreas
{
    // Sun over the horizon: the morning, and what the app is for.
    private const string TodayGlyph =
        "M2 17h20v1.6H2zm2.6 3h16.8v1.6H4.6zM12 4.2a6 6 0 0 1 6 6c0 .6-.07 1.2-.2 1.8H6.2A6.4 6.4 "
        + "0 0 1 6 10.2a6 6 0 0 1 6-6zM11.2 0h1.6v3h-1.6zM3.1 3.4l1.1-1.1 2.1 2.1-1.1 1.1zm15.6 1 "
        + "2.1-2.1 1.1 1.1-2.1 2.1z";

    // A ticked line over three unticked ones.
    private const string TasksGlyph =
        "M2.2 5.6 4 7.4l3.6-3.6 1.2 1.2L4 9.8 1 6.8zM11 5h12v1.8H11zM2 12h7v1.8H2zm9 0h12v1.8H11zM2 "
        + "18.4h7v1.8H2zm9 0h12v1.8H11z";

    // A month grid with its two hanging rings.
    private const string CalendarGlyph =
        "M4 3h1.8v2H4zm14.2 0H20v2h-1.8zM2 6h20v16H2zm1.8 4v10h16.4V10zM6 12h3v2.4H6zm4.5 0h3v2.4h-3zm4.5 "
        + "0h3v2.4h-3zM6 16.4h3v2.4H6zm4.5 0h3v2.4h-3z";

    // Three sliders. Not a cog: the suite's settings are choices, not machinery.
    private const string SettingsGlyph =
        "M3 5.2h8v1.8H3zm11.5 0H21v1.8h-6.5zM12 2.6h1.8v7H12zM3 11.1h4v1.8H3zm7.5 0H21v1.8H10.5zM8 "
        + "8.5h1.8v7H8zM3 17h10v1.8H3zm12.5 0H21v1.8h-5.5zM13 14.4h1.8v7H13z";

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
        new("Today", TodayGlyph, Identity, IdentityCloth),
        new("Tasks", TasksGlyph,
            PaperResources.Colour("ActionMark"), PaperResources.Colour("ActionFill")),
        new("Calendar", CalendarGlyph,
            PaperResources.Colour("ReferenceMark"), PaperResources.Colour("ReferenceFill")),
        new("Settings", SettingsGlyph,
            PaperResources.Colour("ResolvedMark"), PaperResources.Colour("ResolvedFill")),
    ];
}
