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

/// <summary>
/// What The Day's binder tabs are called and where they link (§10). The strip itself — its geometry and the rule that
/// the active tab alone crosses the binder edge — is the suite's, <c>Storyworthtelling.Paper.Binder.BinderStrip</c>,
/// shared with every document sent to the tablet.
/// </summary>
public static class SectionTabs
{
    /// <summary>Where The Day's strip starts, level with the voice line under the header.</summary>
    public const float FirstTabTop = 96f;

    /// <summary>
    /// The name a link and its anchor have to agree on. Both ends come from here because a
    /// disagreement is silent — the tab is drawn, it is a real link, and it goes nowhere.
    /// </summary>
    public static string Destination(DaySection section) => $"foreword-{section}".ToLowerInvariant();

    public static string Label(DaySection section) => section.ToString();
}
