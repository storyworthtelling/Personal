namespace Pulp.Domain.Shell;

/// <summary>The six places Pulp's binder can turn to, in strip order (mockup 01, D212).</summary>
public enum PulpArea
{
    Watching,
    Playlists,
    Discover,
    Sports,
    Calendar,
    Settings,
}

/// <summary>
/// The hue an area's tab is cut from (DESIGN.md §9): the app's identity, or one of the suite's role
/// hues. <b>There is deliberately no Caution member</b>: Pulp's amber identity sits 14.3 degrees from
/// the caution ochre, a recorded departure that is safe only while Pulp draws no Caution anywhere
/// (D275), so a tab cannot be given it without first adding it here.
/// </summary>
public enum AreaHue
{
    Identity,
    Reference,
    Positive,
    Action,
    Progress,
    Resolved,
}

/// <summary>
/// One binder tab: the short <paramref name="Label"/> under the glyph, the Serif
/// <paramref name="Title"/> the page opens with, the drawable named by <paramref name="Glyph"/>, the
/// tab's <paramref name="Hue"/>, and the quiet <paramref name="Status"/> sentence the page shows
/// until its screen is built — a sentence, never a spinner (DESIGN.md §5).
/// </summary>
public sealed record PulpAreaSpec(PulpArea Area, string Label, string Title, string Glyph, AreaHue Hue, string Status);

/// <summary>
/// Pulp's binder strip, decided once and away from the device so the §9 rules can be tested for
/// real (<c>PulpAreaHueTests</c>). The Android shell builds its tabs from this table and never
/// carries a second copy of who is who.
///
/// <para>The hues are the ones drawn on mockup 01 and approved with it (D212): the home in the amber
/// identity, Playlists the reference slate, Discover the positive leaf, Sports the action pine,
/// Calendar the in-progress teal, Settings the neutral grey.</para>
/// </summary>
public static class PulpAreas
{
    public static IReadOnlyList<PulpAreaSpec> All { get; } =
    [
        // "Continue watching" is too long for a 66dp tab, so the tab says Watching (mockup 01).
        new(PulpArea.Watching, "Watching", "Continue watching", "tabwatching", AreaHue.Identity,
            "Shows you are partway through will gather here."),
        new(PulpArea.Playlists, "Playlists", "Playlists", "tabplaylists", AreaHue.Reference,
            "Your playlists will gather here."),
        new(PulpArea.Discover, "Discover", "Discover", "tabdiscover", AreaHue.Positive,
            "The Friday stack will be dealt here."),
        new(PulpArea.Sports, "Sports", "Sports", "tabsports", AreaHue.Action,
            "Your leagues will gather here."),
        new(PulpArea.Calendar, "Calendar", "Calendar", "tabcalendar", AreaHue.Progress,
            "The next two weeks will gather here."),
        new(PulpArea.Settings, "Settings", "Settings", "tabsettings", AreaHue.Resolved,
            "Services, household and children will be set here."),
    ];

    public static PulpAreaSpec For(PulpArea area) => All.Single(spec => spec.Area == area);

    /// <summary>
    /// The hue of the marker underline under an area's header. §9: a tab's role hue is a tab, not a
    /// state, so the page does not inherit it — every content area's rule is the amber identity
    /// (mockups 03, 04, 07, 08), and Settings, whose tab is the neutral grey, rules in that grey
    /// (mockup 09).
    /// </summary>
    public static AreaHue RuleHue(PulpArea area) =>
        area == PulpArea.Settings ? AreaHue.Resolved : AreaHue.Identity;
}
