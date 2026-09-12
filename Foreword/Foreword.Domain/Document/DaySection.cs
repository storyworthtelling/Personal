namespace Foreword.Domain.Document;

/// <summary>
/// The four pages of The Day, in the order they are bound.
///
/// <para>They were four separate documents until P9. Four files on a tablet are four things to find
/// in a list; four tabbed pages are one thing to open, and the tabs make the other three a tap
/// away.</para>
/// </summary>
public enum DaySection
{
    Day,
    Weather,
    Outlook,
    Checklist,
}

/// <summary>One tab, in page points, with the page's top-left as the origin.</summary>
public readonly record struct TabBox(
    DaySection Section, float Left, float Top, float Width, float Height, bool Active);

/// <summary>
/// Where the binder tabs go down the right edge of every page (§10).
///
/// <para>Square on the page side so the seam closes and rounded on the outer side only; the active
/// tab reaches one hairline further in, covering the binder edge, so its seam opens onto the sheet
/// and the others read as sitting behind it. That is the whole grammar of a binder.</para>
/// </summary>
public static class SectionTabs
{
    public const float TabWidth = 24f;
    public const float TabHeight = 72f;
    public const float Gap = 8f;
    public const float FirstTabTop = 96f;
    public const float Radius = 9f;
    public const float Hairline = 1f;

    /// <summary>The hairline the inactive tabs sit behind, as a distance in from the right edge.</summary>
    public const float BinderEdgeInset = TabWidth;

    /// <summary>
    /// The binder grammar as arithmetic, in the one place it is written.
    ///
    /// <para>Two documents carry this strip — The Day's four tabs and Prayer's two — and the rule
    /// that matters is subtle: the active tab alone crosses the binder edge, so it alone touches the
    /// page and the others read as sitting behind it. A second copy of that is a second chance to
    /// close the seam on the wrong side of one document, which nothing fails over.</para>
    /// </summary>
    public static (float Left, float Top, float Width, float Height) Box(
        float pageWidth, int index, bool isActive, float firstTabTop) =>
        (
            pageWidth - TabWidth - (isActive ? Hairline : 0),
            firstTabTop + index * (TabHeight + Gap),
            TabWidth + (isActive ? Hairline : 0),
            TabHeight
        );

    public static IReadOnlyList<TabBox> Layout(float pageWidth, DaySection active)
    {
        var sections = Enum.GetValues<DaySection>();
        var tabs = new List<TabBox>(sections.Length);

        for (var i = 0; i < sections.Length; i++)
        {
            var isActive = sections[i] == active;
            var box = Box(pageWidth, i, isActive, FirstTabTop);

            tabs.Add(new TabBox(
                sections[i], box.Left, box.Top, box.Width, box.Height, isActive));
        }

        return tabs;
    }

    /// <summary>
    /// The name a link and its anchor have to agree on. Both ends come from here because a
    /// disagreement is silent — the tab is drawn, it is a real link, and it goes nowhere.
    /// </summary>
    public static string Destination(DaySection section) => $"foreword-{section}".ToLowerInvariant();

    public static string Label(DaySection section) => section.ToString();
}
