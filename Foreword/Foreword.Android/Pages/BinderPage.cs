using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;

namespace Foreword.Android.Pages;

/// <summary>
/// The shape every screen in Foreword has: an in-page header with its area's rule, the content, the
/// binder strip down the right, and a pinned bottom bar under both (DESIGN.md §5, §7, §9, §10).
///
/// <para>It is a base page rather than something each screen assembles, because the shipped app is
/// what happens when it is optional: every page had a centred Display label, none had a rule, none
/// had the strip, and two of the four areas were reachable only as quiet buttons in a bottom bar —
/// which put Calendar three taps deep inside Settings, where the mockups have it as one of the four
/// things the app <em>is</em>.</para>
///
/// <para>A screen keeps its own XAML and calls <see cref="Wrap"/> after <c>InitializeComponent</c>:
/// whatever the XAML built becomes the content, and the frame is put around it. That is what lets
/// fourteen screens gain the strip without fourteen rewrites.</para>
///
/// <para>The frame itself is the kit's <see cref="PaperBinderPage"/> (audit W8.3a); this keeps only
/// Foreword's area table and where each tab leads.</para>
///
/// <para>There is no navigation bar. The header <em>is</em> the header — a second title in a system
/// bar above it would say the same word twice, in the wrong face.</para>
/// </summary>
public abstract class BinderPage : PaperBinderPage
{
    /// <summary>Which tab is lit. A detail screen lights the area it was opened from.</summary>
    protected ForewordArea Area => (ForewordArea)AreaIndex;

    /// <summary>
    /// Puts the frame around whatever this page's XAML built. Call it once, after
    /// <c>InitializeComponent</c>.
    /// </summary>
    protected void Wrap(ForewordArea area, string title) =>
        Frame(ForewordAreas.All, (int)area, title, ForewordAreas.RuleInk(area));

    /// <summary>
    /// Turning to an area means turning to <b>its root</b>, which is why this pops rather than
    /// pushing on top: a strip that stacked screens would grow a back history the tabs then
    /// contradict. Tapping the area you are already in is how a detail screen returns to it.
    /// </summary>
    protected override async void OnAreaSelected(int index)
    {
        var chosen = (ForewordArea)index;

        await TurnToAreaAsync(chosen == ForewordArea.Today ? null : Root(chosen));
    }

    private static Page Root(ForewordArea area) => area switch
    {
        ForewordArea.Tasks => new TasksPage(),
        ForewordArea.Calendar => new CalendarsPage(),
        ForewordArea.Settings => new SetupPage(),
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Today is the navigation root."),
    };
}
