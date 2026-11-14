using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Diagnostics;
using Storyworthtelling.Maui.State;
using Storyworthtelling.Maui.Views;
using Storyworthtelling.Folds;

namespace Folio.Android.Pages;

/// <summary>
/// The shape every screen in Folio has: an in-page header with its area's rule, the content, the
/// binder strip down the right, and a pinned bottom bar (DESIGN.md §5, §7, §9, §10).
///
/// <para>A screen keeps its own XAML and calls <see cref="Wrap"/> after <c>InitializeComponent</c>,
/// so a screen gains the frame without being rewritten around it.</para>
///
/// <para><b>Two deliberate differences from the drawing</b>, both recorded rather than decided in
/// passing. The tabs are the kit's <c>BinderTabLayout</c> geometry — 66dp tall, a one-tab lead-in, a
/// gap between them — not the mockup's 76dp abutting strip starting 104dp down: the strip is one
/// component across five apps, and Folio's reaching differently from Score's and Post's would cost
/// more than the drawing gains. The bar follows the suite rather than the drawing: the primary on the
/// right (owner's ruling, audit W8.3a), and the strip running to the foot beside it.</para>
///
/// <para>The frame itself is the kit's <see cref="PaperBinderPage"/>; this keeps Folio's area table, where
/// each tab leads, and the deferred half of a hard delete.</para>
///
/// <para>There is no navigation bar. The header <em>is</em> the header.</para>
/// </summary>
public abstract class FolioBinderPage : PaperBinderPage
{
    /// <summary>Which tab is lit. A detail screen lights the area it was opened from.</summary>
    protected FolioArea Area => (FolioArea)AreaIndex;

    /// <summary>
    /// Puts the frame around whatever this page's XAML built. Call it once, after
    /// <c>InitializeComponent</c>.
    /// </summary>
    protected void Wrap(FolioArea area, string title) =>
        Frame(FolioAreas.All, (int)area, title, FolioAreas.RuleInk(area));

    /// <summary>Puts an action card over the whole screen, binder and bar included.</summary>
    protected void Present(View card) => ShowOverlay(card);

    /// <summary>Takes the card away. Safe to call when there is none.</summary>
    protected void Dismiss() => HideOverlay();

    /// <summary>
    /// The deferred half of a destructive act nothing can literally put back (a hard delete: rows
    /// gone, files gone), for a call site that already did the reversible half — hid the row,
    /// soft-deleted it — before calling <see cref="OfferUndo"/> the ordinary way.
    ///
    /// <para><see cref="OfferUndo"/> alone is enough for every other destructive act here, because
    /// shelving or replacing a cover is something a second write undoes. A delete is not, so the
    /// irreversible half waits until the window has actually closed rather than running up front:
    /// <c>DogEarState.BeginUndo</c>'s own rule for a fold nobody turned ("it simply becomes
    /// permanent") applied going forward instead of after the fact.</para>
    /// </summary>
    protected void CommitUnlessUndone(Func<bool> wasUndone, Func<Task> commit) =>
        _ = CommitAfterTheWindowAsync(wasUndone, commit);

    // Awaited end to end, so a failed hard delete reaches the log instead of vanishing inside an unobserved
    // Task<Task>, which is what the old delay-then-continue chain returned (audit 01 §16).
    private static async Task CommitAfterTheWindowAsync(Func<bool> wasUndone, Func<Task> commit)
    {
        await Task.Delay(FoldTiming.UndoWindow);
        if (wasUndone())
        {
            return;
        }

        try
        {
            await commit();
        }
        catch (Exception e)
        {
            MauiLog.Current?.Write("Folio", Microsoft.Extensions.Logging.LogLevel.Error,
                "A deleted item's undo window closed but the delete itself failed; it is hidden and may come back.", e);
        }
    }

    /// <summary>
    /// Turning to an area means turning to <b>its root</b>, which is why this pops rather than
    /// pushing on top: a strip that stacked screens would grow a back history the tabs then
    /// contradict. Tapping the area you are already in is how a detail screen returns to it.
    /// </summary>
    protected override async void OnAreaSelected(int index)
    {
        var chosen = (FolioArea)index;

        await Navigation.PopToRootAsync(animated: false);

        if (chosen != FolioArea.Work)
        {
            await Navigation.PushAsync(Root(chosen), animated: false);
        }
    }

    private static Page Root(FolioArea area) => area switch
    {
        FolioArea.Shelf => new ShelfPage(),
        FolioArea.Settings =>
            IPlatformApplication.Current!.Services.GetRequiredService<SetupPage>(),
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Work is the navigation root."),
    };
}
