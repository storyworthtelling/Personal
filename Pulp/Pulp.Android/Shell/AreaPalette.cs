using Android.Graphics;
using Codex.Domain.Design;
using Pulp.Domain.Shell;

namespace Pulp.Shell;

/// <summary>
/// Paints <see cref="PulpAreas"/>' decisions: which tiers of a hue a tab and a header rule take,
/// and which drawable is each area's glyph. Every arm names one role's tiers only, and none is
/// Caution (D275) — <c>PulpAreaHueTests</c> reads this file to hold both.
///
/// <para>The tiers are the ones mockup 01 draws. An active tab's glyph and label sit on paper at
/// label size, so a role's ink is its <b>Text</b> tier (the text-bearing one) and the identity's is
/// its Mark (the amber's own text tier is a darker brown than the drawing). The cloth an inactive tab
/// is cut from is the <b>Fill</b> tier, as Score and Post cut theirs. A header rule is the Mark.</para>
/// </summary>
internal static class AreaPalette
{
    public static (Color Ink, Color Cloth, Color Rule) For(AreaHue hue) => hue switch
    {
        AreaHue.Identity => (Parse(DesignTokens.Identity.Pulp.Mark), Parse(DesignTokens.Identity.Pulp.Fill), Parse(DesignTokens.Identity.Pulp.Mark)),
        AreaHue.Reference => (Parse(DesignTokens.ReferenceText), Parse(DesignTokens.ReferenceFill), Parse(DesignTokens.ReferenceMark)),
        AreaHue.Positive => (Parse(DesignTokens.PositiveText), Parse(DesignTokens.PositiveFill), Parse(DesignTokens.PositiveMark)),
        AreaHue.Action => (Parse(DesignTokens.ActionText), Parse(DesignTokens.ActionFill), Parse(DesignTokens.ActionMark)),
        AreaHue.Progress => (Parse(DesignTokens.ProgressText), Parse(DesignTokens.ProgressFill), Parse(DesignTokens.ProgressMark)),
        AreaHue.Resolved => (Parse(DesignTokens.ResolvedText), Parse(DesignTokens.ResolvedFill), Parse(DesignTokens.ResolvedMark)),
        _ => throw new ArgumentOutOfRangeException(nameof(hue), hue, "Pulp has no tab colour for this hue."),
    };

    public static int Glyph(PulpArea area) => area switch
    {
        PulpArea.Watching => Resource.Drawable.tabwatching,
        PulpArea.Playlists => Resource.Drawable.tabplaylists,
        PulpArea.Discover => Resource.Drawable.tabdiscover,
        PulpArea.Sports => Resource.Drawable.tabsports,
        PulpArea.Calendar => Resource.Drawable.tabcalendar,
        PulpArea.Settings => Resource.Drawable.tabsettings,
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Pulp has no glyph for this area."),
    };

    private static Color Parse(string token) => Color.ParseColor(token);
}
