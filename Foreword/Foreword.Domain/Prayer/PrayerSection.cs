using Foreword.Domain.Document;

namespace Foreword.Domain.Prayer;

/// <summary>The two pages of the prayer document, in the order they are bound.</summary>
public enum PrayerSection
{
    List,
    Journal,
}

/// <summary>One prayer tab, in page points, with the page's top-left as the origin.</summary>
public readonly record struct PrayerTabBox(
    PrayerSection Section, float Left, float Top, float Width, float Height, bool Active);

/// <summary>
/// Where the prayer document's two binder tabs go (§10).
///
/// <para>Same grammar as The Day's four, and deliberately the same arithmetic:
/// <see cref="SectionTabs.Box"/> is the one place the rule that the active tab alone crosses the
/// binder edge is written down. What differs is where the strip starts, because this page's header
/// sits lower.</para>
/// </summary>
public static class PrayerTabs
{
    /// <summary>
    /// Lower than The Day's, so the first tab clears the rule under the date. A strip that starts
    /// level with the title reads as floating over the page rather than attached to it.
    /// </summary>
    public const float FirstTabTop = 120f;

    public static IReadOnlyList<PrayerTabBox> Layout(float pageWidth, PrayerSection active)
    {
        var sections = Enum.GetValues<PrayerSection>();
        var tabs = new List<PrayerTabBox>(sections.Length);

        for (var i = 0; i < sections.Length; i++)
        {
            var isActive = sections[i] == active;
            var box = SectionTabs.Box(pageWidth, i, isActive, FirstTabTop);

            tabs.Add(new PrayerTabBox(
                sections[i], box.Left, box.Top, box.Width, box.Height, isActive));
        }

        return tabs;
    }

    /// <summary>
    /// The name a link and its anchor have to agree on. Namespaced away from The Day's so the two
    /// documents can never share a destination — a collision there would be a tab that jumps to the
    /// wrong page, silently, on whichever document was merged second.
    /// </summary>
    public static string Destination(PrayerSection section) =>
        $"foreword-prayer-{section}".ToLowerInvariant();

    public static string Label(PrayerSection section) => section.ToString();
}
