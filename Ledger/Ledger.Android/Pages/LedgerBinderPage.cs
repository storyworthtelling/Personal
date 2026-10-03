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
public abstract class LedgerBinderPage : ContentPage
{
    private readonly BinderTabs strip = new();
    private readonly PaperPageHeader header = new();
    private readonly ContentView well = new();
    private readonly ContentView barSlot = new();

    // The dog-ear's place: the page's own top-right corner, which is the right edge of the content
    // column and so flush to the binder gutter (mockup 17: top 0, right 66dp). It spans the header and
    // the well rather than living in the well, where it sat below the header and over the first card.
    private readonly ContentView cornerSlot = new()
    {
        HorizontalOptions = LayoutOptions.End,
        VerticalOptions = LayoutOptions.Start,
    };

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

        keyboardListener = new KeyboardInsetsListener(open => barSlot.IsVisible = !open);
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
        barSlot.IsVisible = true; // the bar is never left hidden by a page that has gone
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

    /// <summary>Back on a page that commits before leaving: finish, then pop. A page that does not asks
    /// nothing of this.</summary>
    protected override bool OnBackButtonPressed()
    {
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
    protected LedgerArea Area { get; private set; }

    /// <summary>
    /// Puts the frame around whatever this page's XAML built. Call it once, after
    /// <c>InitializeComponent</c>. <see cref="PaperPageHeader"/> reads its own "DisplayLabel"
    /// style through <c>PaperResources</c>, which walks the app's merged resources rather than
    /// any one dictionary's own entries — so it resolves against the kit's Paper.xaml, merged in
    /// App.xaml.cs (Release B).
    /// </summary>
    protected void Wrap(LedgerArea area, string title)
    {
        Area = area;
        header.Text = title;
        header.Ink = LedgerAreas.RuleInk(area);

        well.Content = Content;

        strip.SetAreas(LedgerAreas.All, (int)area);
        strip.AreaSelected += OnAreaSelected;

        var page = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)],
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(BinderTabs.WidthDp)),
            ],
        };

        var column = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
            Children = { header, well },
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(well, 1);

        // The strip runs beside the header and the content but stops at the bar, which spans the
        // whole width: the bar belongs to the page you are on, not to the binder.
        page.Children.Add(column);
        page.Children.Add(strip);
        page.Children.Add(cornerSlot);
        page.Children.Add(barSlot);
        Grid.SetRow(cornerSlot, 0);
        Grid.SetColumn(cornerSlot, 0);
        Grid.SetRow(column, 0);
        Grid.SetColumn(column, 0);
        Grid.SetRow(strip, 0);
        Grid.SetColumn(strip, 1);
        Grid.SetRow(barSlot, 1);
        Grid.SetColumn(barSlot, 0);
        Grid.SetColumnSpan(barSlot, 2);

        Content = page;
    }

    /// <summary>
    /// Puts the page's dog-ear in its corner. Call it with the page's <c>DogEar</c> once; the frame owns
    /// the placement so no screen decides for itself where the corner is.
    /// </summary>
    protected void SetCorner(View ear) => cornerSlot.Content = ear;

    /// <summary>
    /// The pinned bar (§7). Buttons in order, the primary one last — it takes the width that is
    /// left, so the one thing the screen is for is the widest target on it. No buttons clears it.
    /// </summary>
    protected void SetBar(params View[] buttons)
    {
        if (buttons.Length == 0)
        {
            barSlot.Content = null;
            return;
        }

        var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(24, 12) };

        for (var i = 0; i < buttons.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition(
                i == buttons.Length - 1 ? GridLength.Star : GridLength.Auto));
            row.Children.Add(buttons[i]);
            Grid.SetColumn(buttons[i], i);
        }

        barSlot.Content = new VerticalStackLayout
        {
            Children =
            {
                new BoxView
                {
                    HeightRequest = 1,
                    // Not the indexer: it looks only at a dictionary's own entries, while
                    // PaperResources.Colour walks what has been merged into it — and "Hairline"
                    // now arrives from the kit by merge, not from a dictionary this head owns.
                    Color = PaperResources.Colour("Hairline"),
                },
                row,
            },
        };
    }

    /// <summary>
    /// Turning to an area means turning to <b>its root</b>, which is why this pops rather than
    /// pushing on top: a strip that stacked screens would grow a back history the tabs then
    /// contradict. Tapping the area you are already in is how a detail screen (Connect an
    /// account, under Settings) returns to it.
    /// </summary>
    private async void OnAreaSelected(int index)
    {
        var chosen = (LedgerArea)index;

        if (CommitsBeforeLeaving)
        {
            await BeforeLeavingAsync();
        }

        await Navigation.PopToRootAsync(animated: false);

        if (chosen != LedgerArea.Accounts)
        {
            await Navigation.PushAsync(Root(chosen), animated: false);
        }
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
