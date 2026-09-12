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
/// <para>There is no navigation bar. The header <em>is</em> the header — a second title in a system
/// bar above it would say the same word twice, in the wrong face.</para>
/// </summary>
public abstract class BinderPage : ContentPage
{
    private readonly BinderTabs strip = new();
    private readonly PaperPageHeader header = new();
    private readonly ContentView well = new();
    private readonly ContentView barSlot = new();

    protected BinderPage()
    {
        BackgroundColor = PaperResources.Colour("Paper");
        NavigationPage.SetHasNavigationBar(this, false);
    }

    /// <summary>Which tab is lit. A detail screen lights the area it was opened from.</summary>
    protected ForewordArea Area { get; private set; }

    protected string HeaderText
    {
        get => header.Text;
        set => header.Text = value;
    }

    /// <summary>
    /// Puts the frame around whatever this page's XAML built. Call it once, after
    /// <c>InitializeComponent</c>.
    /// </summary>
    protected void Wrap(ForewordArea area, string title)
    {
        Area = area;
        header.Text = title;
        header.Ink = ForewordAreas.RuleInk(area);

        well.Content = Content;

        strip.SetAreas(ForewordAreas.All, (int)area);
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

        var row = new Grid
        {
            ColumnSpacing = DesignTokens.Space.X2,
            Padding = new Thickness(
                DesignTokens.Space.X6, DesignTokens.Space.X3, DesignTokens.Space.X6, DesignTokens.Space.X4),
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
        var chosen = (ForewordArea)index;

        await Navigation.PopToRootAsync(animated: false);

        if (chosen != ForewordArea.Today)
        {
            await Navigation.PushAsync(Root(chosen), animated: false);
        }
    }

    private static Page Root(ForewordArea area) => area switch
    {
        ForewordArea.Tasks => new TasksPage(),
        ForewordArea.Calendar => new CalendarsPage(),
        ForewordArea.Settings => new SetupPage(),
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Today is the navigation root."),
    };
}
