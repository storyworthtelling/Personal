using Index.Domain.Design;
using Microsoft.Maui.ApplicationModel;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;

namespace Index.Android.Pages;

/// <summary>
/// The shape every screen in Ledger has: an in-page header with its area's rule, the content, and
/// the binder strip down the right (DESIGN.md §5, §7, §9) — Ledger's first real adoption of the
/// shared <c>Storyworthtelling.Maui</c> kit's <see cref="BinderTabs"/>, following the same pattern
/// <c>Foreword.Android.Pages.BinderPage</c> and <c>Folio.Android.Pages.FolioBinderPage</c> already
/// use (see those for the fuller remarks this repeats the shape of).
///
/// <para>Before this, both of Ledger's screens navigated by a plain bottom-bar button
/// ("Settings" on Balances) — the exact P9 failure Foreword shipped once already: a binder
/// component built and wired to nothing.</para>
///
/// <para>A screen keeps its own XAML and calls <see cref="Wrap"/> after <c>InitializeComponent</c>:
/// whatever the XAML built becomes the content, and the frame is put around it.</para>
///
/// <para>There is no navigation bar. The header <em>is</em> the header.</para>
/// </summary>
public abstract class LedgerBinderPage : PaperBinderPage
{
    // Set while this page has taken over the window's soft-input mode and the insets listener (see
    // HidesBarForKeyboard); remembered so the way the window was before is put back.
    private KeyboardInsetsListener? keyboardListener;
    private global::Android.Views.SoftInput? softInputBefore;

    protected LedgerBinderPage()
    {
        NavigationPage.SetHasNavigationBar(this, false);
    }

    /// <summary>
    /// True on a page with text fields, which asks for what mockups 4 and 15 draw: "the keyboard pushes the
    /// page up so the field being typed in stays in view; the pinned bar hides while it's open". While the
    /// page is showing it sets the window to resize for the keyboard (so the page's scroll view is what
    /// shrinks, and the focused field can be scrolled into the space that is left) and listens for the
    /// keyboard's insets, hiding the bar slot while the keyboard is up and showing it again after. The bar's
    /// own content is never touched, so what <see cref="SetBar"/> set is exactly what comes back.
    /// </summary>
    protected virtual bool HidesBarForKeyboard => false;

    /// <summary>
    /// True on a page whose graph paper runs up behind the header, as every Accounts screen draws it
    /// (mockups 2, 4, 10, 14, 17: the lines continue under the title and its rule). The frame lays the paper
    /// under the header AND the well from one origin, so a page that sets this must not add a
    /// <see cref="Views.GraphPaperBackground"/> of its own in the well, where it would start below the
    /// header on a different grid. A page that does not set it keeps drawing its own, as before.
    /// </summary>
    protected virtual bool PaperBehindHeader => false;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (HidesBarForKeyboard)
        {
            AttachKeyboard();
        }
    }

    protected override void OnDisappearing()
    {
        DetachKeyboard();
        base.OnDisappearing();
    }

    private void AttachKeyboard()
    {
        var window = Platform.CurrentActivity?.Window;
        if (window is null || keyboardListener is not null)
        {
            return;
        }

        softInputBefore = window.Attributes?.SoftInputMode;
        window.SetSoftInputMode(global::Android.Views.SoftInput.AdjustResize);

        keyboardListener = new KeyboardInsetsListener(open => ShowBar(!open));
        window.DecorView.SetOnApplyWindowInsetsListener(keyboardListener);
    }

    private void DetachKeyboard()
    {
        var window = Platform.CurrentActivity?.Window;
        if (keyboardListener is null)
        {
            return;
        }

        window?.DecorView.SetOnApplyWindowInsetsListener(null);
        if (window is not null && softInputBefore is { } before)
        {
            window.SetSoftInputMode(before);
        }

        keyboardListener.Dispose();
        keyboardListener = null;
        softInputBefore = null;
        ShowBar(true); // the bar is never left hidden by a page that has gone
    }

    /// <summary>
    /// Keeps a field in view while it is typed in: when <paramref name="entry"/> takes focus, the page's
    /// scroll view scrolls it into the part of the page the keyboard has left. Waits a beat for the
    /// keyboard to finish opening, because the viewport it scrolls within is the shrunken one.
    /// </summary>
    protected void KeepVisibleWhenFocused(ScrollView scroll, Entry entry)
    {
        entry.Focused += (_, _) => Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(250), () => _ = scroll.ScrollToAsync(entry, ScrollToPosition.MakeVisible, animated: true));
    }

    /// <summary>
    /// True on a page that has work to finish before it is left (Settings: the fields it saves as they
    /// are left). Turning a tab, or pressing back, then waits for <see cref="BeforeLeavingAsync"/> first.
    /// </summary>
    protected virtual bool CommitsBeforeLeaving => false;

    /// <summary>What a page with <see cref="CommitsBeforeLeaving"/> finishes before it goes.</summary>
    protected virtual Task BeforeLeavingAsync() => Task.CompletedTask;

    /// <summary>
    /// While an action card's haze is up, back dismisses the card and stays on the page
    /// (<see cref="OverlayBackPress"/>). Otherwise, on a page that commits before leaving: finish, then pop.
    /// A page that does not asks nothing of this.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        var overlayAction = OverlayBackPress.Decide(OverlayShowing);
        if (OverlayBackPress.Consumes(overlayAction))
        {
            HideOverlay();
            return true; // handled: the card is gone, the page stays
        }

        if (!CommitsBeforeLeaving)
        {
            return base.OnBackButtonPressed();
        }

        _ = PopAfterCommitAsync();
        return true; // handled: the pop happens once the commit has
    }

    private async Task PopAfterCommitAsync()
    {
        await BeforeLeavingAsync();
        if (Navigation.NavigationStack.Count > 1)
        {
            await Navigation.PopAsync();
        }
    }

    /// <summary>Which tab is lit. A detail screen lights the area it was opened from.</summary>
    protected LedgerArea Area => (LedgerArea)AreaIndex;

    /// <summary>
    /// Puts the kit's frame around whatever this page's XAML built (audit W8.3a: this was Index's own copy
    /// of it). Call it once, after <c>InitializeComponent</c>.
    /// </summary>
    protected void Wrap(LedgerArea area, string title)
    {
        if (PaperBehindHeader)
        {
            // Under the header and the well from one origin: one grid from the page's top edge.
            SetBackdrop(new Views.GraphPaperBackground { GroundColour = Color.FromArgb(LedgerAreas.AccountsGroundHex) });
        }

        Frame(LedgerAreas.All, (int)area, title, LedgerAreas.RuleInk(area));
    }

    /// <summary>
    /// Turning to an area means turning to <b>its root</b>, which is why this pops rather than
    /// pushing on top: a strip that stacked screens would grow a back history the tabs then
    /// contradict. Tapping the area you are already in is how a detail screen (Connect an
    /// account, under Settings) returns to it.
    /// </summary>
    protected override async void OnAreaSelected(int index)
    {
        var chosen = (LedgerArea)index;

        if (CommitsBeforeLeaving)
        {
            await BeforeLeavingAsync();
        }

        await TurnToAreaAsync(chosen == LedgerArea.Accounts ? null : Root(chosen));
    }

    // Savings is real as of Release G (mockups 9/16); Snowball (Release D, mockup 6) and Budget
    // (Release E, mockups 7/8) already were.
    private static Page Root(LedgerArea area) => area switch
    {
        LedgerArea.Settings => IPlatformApplication.Current!.Services.GetRequiredService<SetupPage>(),
        LedgerArea.Snowball => IPlatformApplication.Current!.Services.GetRequiredService<SnowballPage>(),
        LedgerArea.Budget => IPlatformApplication.Current!.Services.GetRequiredService<BudgetPage>(),
        LedgerArea.Savings => IPlatformApplication.Current!.Services.GetRequiredService<SavingsPage>(),
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Accounts is the navigation root."),
    };
}

/// <summary>
/// Reports whether the soft keyboard is showing, from the window's insets (API 30's IME inset — the app's
/// floor is 34, see the csproj). Hands the insets straight on to the view's own handling afterwards, so
/// listening changes nothing about how the window lays out.
/// </summary>
internal sealed class KeyboardInsetsListener(Action<bool> changed)
    : Java.Lang.Object, global::Android.Views.View.IOnApplyWindowInsetsListener
{
    private bool? lastOpen;

    public global::Android.Views.WindowInsets OnApplyWindowInsets(
        global::Android.Views.View v, global::Android.Views.WindowInsets insets)
    {
        var open = insets.IsVisible(global::Android.Views.WindowInsets.Type.Ime());
        if (lastOpen != open)
        {
            lastOpen = open;
            changed(open);
        }

        return v.OnApplyWindowInsets(insets) ?? insets;
    }
}
