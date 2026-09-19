using Storyworthtelling.Maui.Views;

namespace Ledger.Android.Pages;

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

    protected LedgerBinderPage()
    {
        NavigationPage.SetHasNavigationBar(this, false);
    }

    /// <summary>Which tab is lit. A detail screen lights the area it was opened from.</summary>
    protected LedgerArea Area { get; private set; }

    /// <summary>
    /// Puts the frame around whatever this page's XAML built. Call it once, after
    /// <c>InitializeComponent</c>. <see cref="PaperPageHeader"/> reads its own "DisplayLabel"
    /// style through <c>PaperResources</c>, which walks the app's merged resources rather than
    /// any one dictionary's own entries — so it resolves against Ledger's existing
    /// DesignTokens.xaml (which registers that same name) without Ledger merging the kit's own
    /// style dictionary.
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
        page.Children.Add(barSlot);
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
                    Color = (Color)Application.Current!.Resources["Hairline"],
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

        await Navigation.PopToRootAsync(animated: false);

        if (chosen != LedgerArea.Balances)
        {
            await Navigation.PushAsync(Root(chosen), animated: false);
        }
    }

    private static Page Root(LedgerArea area) => area switch
    {
        LedgerArea.Settings => IPlatformApplication.Current!.Services.GetRequiredService<SetupPage>(),
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Balances is the navigation root."),
    };
}
