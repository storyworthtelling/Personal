namespace Foreword.Domain.Prayer;

/// <summary>The two pages of the prayer document, in the order they are bound.</summary>
public enum PrayerSection
{
    List,
    Journal,
}

/// <summary>
/// What the prayer document's two binder tabs are called and where they link (§10). Same strip as The Day's, drawn by
/// the suite's <c>BinderStrip</c>; what differs is where it starts, because this page's header sits lower.
/// </summary>
public static class PrayerTabs
{
    /// <summary>
    /// Lower than The Day's, so the first tab clears the rule under the date. A strip that starts
    /// level with the title reads as floating over the page rather than attached to it.
    /// </summary>
    public const float FirstTabTop = 120f;

    /// <summary>
    /// The name a link and its anchor have to agree on. Namespaced away from The Day's so the two
    /// documents can never share a destination — a collision there would be a tab that jumps to the
    /// wrong page, silently, on whichever document was merged second.
    /// </summary>
    public static string Destination(PrayerSection section) =>
        $"foreword-prayer-{section}".ToLowerInvariant();

    public static string Label(PrayerSection section) => section.ToString();
}
