using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>
/// The frame a secondary screen in Score, Post or Plate wears: an in-page header with the
/// hosting area's rule, the content, and the binder tabs down the right edge — the same shape
/// <see cref="BinderTabs"/> already gives each app's top-level shell (<c>ScoreActivity</c>,
/// <c>PostActivity</c>, <c>PlateActivity</c>), reused here for the plain <c>Activity</c> screens
/// those shells launch (Equalizer, Diagnostics, an open email, and so on).
///
/// <para>Those screens used to call <c>SetContentView</c> on their own header-plus-content
/// layout with no binder gutter at all — the exact shape this app's shell warns against
/// elsewhere in the suite (Foreword shipped with a binder strip built and never wired to a
/// page). This is that wiring for the three native heads: a shared painter rather than eleven
/// screens each reassembling the header/tabs/content plumbing their own way.</para>
///
/// <para>Tapping the tab you were opened from returns to it (ordinarily just <c>Finish()</c>,
/// since the host Activity is still on the back stack under this one); tapping a different one
/// asks the caller to switch area there too — each host Activity keeps its own static
/// <c>PendingArea</c> for this (see <c>ScoreActivity.PendingArea</c> for the fuller remarks),
/// since <see cref="BinderFrame"/> itself has no host of its own to hold one.</para>
/// </summary>
public static class BinderFrame
{
    /// <summary>
    /// Builds the page-plus-tabs spread. The caller places it in its own root above its own
    /// bottom bar, exactly as each shell's <c>BuildLayout</c> already does — this only replaces
    /// the header-and-content half of that layout, not the whole screen.
    /// </summary>
    public static View Wrap(
        Activity activity,
        string title,
        Color hue,
        View content,
        IReadOnlyList<BinderArea> areas,
        int activeIndex,
        Action<int> onAreaSelected) =>
        WrapWithOverlay(activity, title, hue, content, areas, activeIndex, onAreaSelected).Spread;

    /// <summary>
    /// Same as <see cref="Wrap"/>, but also hands back the page's own overlay host — the frame
    /// a screen posts a <see cref="DogEar"/> notice or fold into, the way each top-level shell
    /// exposes its own <c>pageOverlayHost</c> to the areas it hosts.
    /// </summary>
    public static (View Spread, FrameLayout Overlay) WrapWithOverlay(
        Activity activity,
        string title,
        Color hue,
        View content,
        IReadOnlyList<BinderArea> areas,
        int activeIndex,
        Action<int> onAreaSelected)
    {
        var edge = PaperTheme.Dp(activity, DesignTokens.Shape.ScreenEdgeDp);

        var page = new LinearLayout(activity) { Orientation = Orientation.Vertical };

        var header = PaperUi.ScreenHeader(activity, title, hue);
        header.SetPadding(edge, PaperTheme.Dp(activity, DesignTokens.Space.X8), edge, 0);
        page.AddView(header);

        var contentHost = new FrameLayout(activity);
        contentHost.SetPadding(edge, 0, edge, 0);
        contentHost.AddView(content, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        page.AddView(contentHost, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        var spread = new LinearLayout(activity) { Orientation = Orientation.Horizontal };

        // Padded down by the status-bar/display-cutout inset under enforced edge-to-edge
        // (API 35+), matching every top-level shell's own page — a secondary screen's header
        // was drawn partly behind the clock/battery icons before this existed. Only the page
        // gets the inset, not the tab strip beside it, for the same reason the shells don't
        // inset their own binder either.
        var overlay = PaperUi.WrapForTopInset(activity, page);
        spread.AddView(overlay, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.MatchParent) { Weight = 1 });

        var binder = new BinderTabs(activity);
        binder.SetAreas(areas, activeIndex);
        binder.AreaSelected += index => onAreaSelected(index);
        spread.AddView(binder, new LinearLayout.LayoutParams(
            PaperTheme.Dp(activity, BinderTabs.WidthDp), ViewGroup.LayoutParams.MatchParent));

        return (spread, overlay);
    }
}
