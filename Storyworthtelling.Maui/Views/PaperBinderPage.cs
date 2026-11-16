using Storyworthtelling.Folds;
using Storyworthtelling.Maui.Health;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// The frame every MAUI screen wears (DESIGN.md §5, §7, §9, §13): the in-page Serif header with its
/// area's rule, the content well, the binder strip down the right running to the foot of the screen, the
/// pinned bar under the page, the dog-ear in the page's own top corner beside the binder, and a slot over
/// all of it for a paper card and its haze.
///
/// <para><b>Why the kit owns it (audit W8.3a, report 14).</b> Index, Folio and Foreword each built their
/// own copy and the copies drifted: the strip stopped above the bar in two and left a paper hole beside
/// it in the third; the primary button led in one and trailed in two; the bar had three paddings; the
/// dog-ear sat in a corner slot, in the frame, or inline in the scrolling content. A head now keeps only
/// what is its own — its area table, where a tab leads, and its own extras (Index's commit-before-leaving
/// and keyboard, Folio's deferred delete) — in a thin subclass.</para>
///
/// <para><b>The strip runs to the foot</b> (owner's ruling, report 14 §1): the bar belongs to the page
/// column only, so the gutter is cloth from top to bottom on every screen of every app.</para>
///
/// <para>A screen keeps its own XAML and the head's <c>Wrap</c> calls <see cref="Frame"/> after
/// <c>InitializeComponent</c>: whatever the XAML built becomes the well's content.</para>
/// </summary>
public abstract class PaperBinderPage : ContentPage, ICardHost
{
    private readonly BinderTabs strip = new();
    private readonly PaperPageHeader header = new();
    private readonly ContentView well = new();
    private readonly ContentView barSlot = new();

    // The dog-ear's place: the page's own top-right corner, flush to the binder gutter (Index mockup 17).
    private readonly ContentView cornerSlot = new()
    {
        HorizontalOptions = LayoutOptions.End,
        VerticalOptions = LayoutOptions.Start,
    };

    // Over the whole page — header, well, strip and bar — where a paper card and its haze go.
    private readonly ContentView overlaySlot = new() { IsVisible = false };

    private readonly DogEarState folds = new();
    private DogEar corner;
    private DogEarState cornerState;
    private Grid? column;
    private View? pendingBackdrop;

    protected PaperBinderPage()
    {
        BackgroundColor = PaperResources.Colour("Paper");
        NavigationPage.SetHasNavigationBar(this, false);

        corner = new DogEar(folds);
        cornerState = folds;
        cornerSlot.Content = corner;
    }

    /// <summary>Which tab is lit, by its index in the head's area table.</summary>
    protected int AreaIndex { get; private set; }

    protected string HeaderText
    {
        get => header.Text;
        set => header.Text = value;
    }

    /// <summary>The frame's own notices and undo, behind the corner unless a page sets its own.</summary>
    protected DogEarState Folds => folds;

    /// <summary>True while a card or its haze is up.</summary>
    protected bool OverlayShowing => overlaySlot.IsVisible;

    /// <summary>Where a tab leads: the head's own navigation (an area's root, or popping to the home area).</summary>
    protected abstract void OnAreaSelected(int index);

    /// <summary>
    /// Turns to an area (DESIGN.md §6, report 04 §24): back to the first page, on to the area's root when it is
    /// not that page (<paramref name="root"/> null), and the page that is now showing settles into place the way
    /// the native page turn does, rather than appearing. The stack itself moves without the platform's slide,
    /// since a pop and a push would otherwise play two transitions for one turn.
    /// </summary>
    protected async Task TurnToAreaAsync(Page? root)
    {
        await Navigation.PopToRootAsync(animated: false);

        if (root is not null)
        {
            await Navigation.PushAsync(root, animated: false);
        }

        (Navigation.NavigationStack.LastOrDefault() as PaperBinderPage)?.SettleIn();
    }

    // The incoming page is revealed under the one turned away: a gentle fade and a slight scale from the
    // spine, as paper_slide_in does on the native heads. A page not yet on screen has nothing to animate.
    private void SettleIn()
    {
        if (well.Handler is null)
        {
            return;
        }

        well.AnchorX = 0;
        well.Opacity = 0.3;
        well.Scale = 0.98;
        _ = well.FadeTo(1, DesignTokens.Motion.StandardMs, Easing.CubicOut);
        _ = well.ScaleTo(1, DesignTokens.Motion.StandardMs, Easing.CubicOut);
    }

    /// <summary>
    /// Puts the frame around whatever this page's XAML built. The head calls it from its <c>Wrap</c>, once,
    /// after <c>InitializeComponent</c>.
    /// </summary>
    protected void Frame(IReadOnlyList<BinderArea> areas, int active, string title, Color ruleInk)
    {
        AreaIndex = active;
        header.Text = title;
        header.Ink = ruleInk;

        // Detach before reparenting: the page's own XAML content is still this page's child.
        var body = Content;
        Content = null;
        well.Content = body;

        strip.SetAreas(areas, active);
        strip.AreaSelected += OnAreaSelected;

        column = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
        };
        column.Children.Add(header);
        column.Children.Add(well);
        Grid.SetRow(header, 0);
        Grid.SetRow(well, 1);

        if (pendingBackdrop is { } early)
        {
            Hang(early);
            pendingBackdrop = null;
        }

        var page = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)],
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(BinderTabs.WidthDp)),
            ],
        };

        // Every screen's ground is paper with its grain (DESIGN.md §2), under the page and its bar.
        var ground = new PaperGround(PaperResources.Colour("Paper"));
        page.Children.Add(ground);
        Grid.SetRowSpan(ground, 2);
        Grid.SetColumn(ground, 0);

        page.Children.Add(column);
        Grid.SetRow(column, 0);
        Grid.SetColumn(column, 0);

        page.Children.Add(cornerSlot);
        Grid.SetRow(cornerSlot, 0);
        Grid.SetColumn(cornerSlot, 0);

        page.Children.Add(barSlot);
        Grid.SetRow(barSlot, 1);
        Grid.SetColumn(barSlot, 0);

        page.Children.Add(strip);
        Grid.SetRow(strip, 0);
        Grid.SetRowSpan(strip, 2);
        Grid.SetColumn(strip, 1);

        page.Children.Add(overlaySlot);
        Grid.SetRowSpan(overlaySlot, 2);
        Grid.SetColumnSpan(overlaySlot, 2);

        Content = page;
    }

    /// <summary>
    /// Puts something behind the header <b>and</b> the well from one origin (Folio's desk, Index's graph
    /// paper). May be called before <see cref="Frame"/>; it is then hung when the frame exists.
    /// </summary>
    protected void SetBackdrop(View backdrop)
    {
        ArgumentNullException.ThrowIfNull(backdrop);
        if (column is null)
        {
            pendingBackdrop = backdrop;
            return;
        }

        Hang(backdrop);
    }

    private void Hang(View backdrop)
    {
        backdrop.InputTransparent = true;
        column!.Children.Insert(0, backdrop);
        Grid.SetRow(backdrop, 0);
        Grid.SetRowSpan(backdrop, 2);
    }

    /// <summary>
    /// The pinned bar (§7), under the page. Buttons in order, the primary one last: it sits on the right
    /// and takes the width that is left, so the one thing the screen is for is the widest target on it.
    /// No buttons clears it.
    /// </summary>
    protected void SetBar(params View[] buttons) => barSlot.Content = PaperBar.Of(buttons);

    /// <summary>Hides or shows the pinned bar without touching what is in it (the keyboard's way).</summary>
    protected void ShowBar(bool visible) => barSlot.IsVisible = visible;

    /// <summary>
    /// Puts a page's own dog-ear in the frame's corner, with the state behind it — a page that keeps its own
    /// notices (Index's Accounts, Foreword's Today) rather than the frame's <see cref="Folds"/>.
    /// </summary>
    protected void SetCorner(DogEar ear, DogEarState state)
    {
        corner = ear;
        cornerState = state;
        cornerSlot.Content = ear;
    }

    /// <summary>States, or withdraws, one notice behind the frame's fold (§13).</summary>
    protected void SetNotice(string key, DogEarNotice? notice)
    {
        folds.SetNotice(key, notice);
        corner.Refresh();
    }

    /// <summary>Does the thing, and offers it back (§7): the fold's window is the confirmation.</summary>
    protected void OfferUndo(string hint, Action reverse)
    {
        folds.BeginUndo(hint, reverse, DateTimeOffset.Now, FoldTiming.UndoWindow);
        corner.Refresh();
    }

    /// <summary>Lays a card and its haze over the whole page. One at a time; a second replaces the first.</summary>
    protected void ShowOverlay(View overlay)
    {
        overlaySlot.Content = overlay;
        overlaySlot.IsVisible = true;
    }

    /// <summary>Takes the card away. Safe to call when there is none.</summary>
    protected void HideOverlay()
    {
        overlaySlot.IsVisible = false;
        overlaySlot.Content = null;
    }

    void ICardHost.ShowCard(View card) => ShowOverlay(card);

    void ICardHost.HideCard() => HideOverlay();

    // Every page carries the sign-in notice when Google turned the background sign-in away (audit 11 §4),
    // and any notice a page raised as it left (CarriedNotices), behind whichever corner it shows.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        GoogleSignInHealth.Apply(cornerState);
        CarriedNotices.Shared.DeliverTo(cornerState);
        corner.Refresh();
    }

    /// <summary>The back gesture puts a card away before it leaves the screen.</summary>
    protected override bool OnBackButtonPressed()
    {
        if (OverlayShowing)
        {
            HideOverlay();
            return true;
        }

        return base.OnBackButtonPressed();
    }
}
