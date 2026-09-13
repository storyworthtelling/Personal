using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;

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
/// more than the drawing gains. The bar, which is this page's own composition rather than anything
/// the kit owns, follows the drawing and stops at the gutter.</para>
///
/// <para>There is no navigation bar. The header <em>is</em> the header.</para>
/// </summary>
public abstract class FolioBinderPage : ContentPage
{
    private readonly BinderTabs strip = new();
    private readonly PaperPageHeader header = new();
    private readonly ContentView well = new();
    private readonly ContentView barSlot = new();

    protected FolioBinderPage()
    {
        BackgroundColor = PaperResources.Colour("Paper");
        NavigationPage.SetHasNavigationBar(this, false);
    }

    /// <summary>Which tab is lit. A detail screen lights the area it was opened from.</summary>
    protected FolioArea Area { get; private set; }

    protected string HeaderText
    {
        get => header.Text;
        set => header.Text = value;
    }

    /// <summary>
    /// Puts the frame around whatever this page's XAML built. Call it once, after
    /// <c>InitializeComponent</c>.
    /// </summary>
    protected void Wrap(FolioArea area, string title)
    {
        Area = area;
        header.Text = title;
        header.Ink = FolioAreas.RuleInk(area);

        well.Content = Content;

        strip.SetAreas(FolioAreas.All, (int)area);
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

        page.Children.Add(column);
        page.Children.Add(strip);
        page.Children.Add(barSlot);
        Grid.SetRow(column, 0);
        Grid.SetColumn(column, 0);
        Grid.SetRow(strip, 0);
        Grid.SetColumn(strip, 1);
        Grid.SetRow(barSlot, 1);
        Grid.SetColumn(barSlot, 0);

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

        var row = new Grid
        {
            ColumnSpacing = DesignTokens.Space.X2,
            Padding = new Thickness(
                DesignTokens.Space.X6, DesignTokens.Space.X3, DesignTokens.Space.X4, DesignTokens.Space.X4),
        };

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
                    HeightRequest = DesignTokens.Shape.HairlineDp,
                    Color = PaperResources.Colour("Hairline"),
                },
                row,
            },
        };
    }

    /// <summary>
    /// Turning to an area means turning to <b>its root</b>, which is why this pops rather than
    /// pushing on top: a strip that stacked screens would grow a back history the tabs then
    /// contradict. Tapping the area you are already in is how a detail screen returns to it.
    /// </summary>
    private async void OnAreaSelected(int index)
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
