using System.Globalization;
using Index.Android.Views;
using Index.Domain.Household;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Infrastructure.Household;
using Index.Infrastructure.Services;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.State;
using Storyworthtelling.Maui.Views;

namespace Index.Android.Pages;

/// <summary>
/// Budget (mockup 7/8, Release E): a month in progress, index-card categories with a shared meter row
/// (D48/D116), a post-it of that category's transactions or its month-by-month history, recategorizing
/// (D58/D67), and the quiet empty state before any worksheet exists (D88). Built the same way
/// <see cref="MainPage"/> is — the XAML shell exists only to satisfy <c>LedgerBinderShellTests</c>;
/// every visible thing here is composed in code.
/// </summary>
public partial class BudgetPage : LedgerBinderPage
{
    private readonly BudgetService budget;
    // Release I: her install renders this month's categories read-only from the household snapshot
    // instead of live Plaid/EF data (D09/D29). See TryLoadHouseholdBudgetIfHersAsync; the owner-install
    // path below (ReloadAsync/RenderMonthAsync) is unchanged.
    private readonly HouseholdSnapshotReader householdReader;
    // The remembered role: no page re-derives it from Drive (Phase 1.1).
    private readonly InstallRoleService roles;

    private readonly DogEarState dogEarState = new();
    private readonly DogEar dogEar;

    private readonly ContentView bodyHost = new();
    private readonly ContentView overlayHost = new() { IsVisible = false };
    private readonly Grid rootLayout = new();

    private HashSet<string> lastNoticeKeys = [];

    // The one status sentence shown while the Plaid pull runs behind the first paint; null when idle.
    private string? refreshStatus;
    private bool refreshInFlight;

    // Which category's post-it is open — only one at a time, the same rule Accounts' own post-it uses.
    private Guid? expandedCategoryId;
    private bool uncategorizedExpanded;

    // How many times the open post-it's trailing "n more" / "Earlier months" line has been tapped. The note
    // lengthens in place by NotePaging's step per tap; opening a different card starts it over.
    private int noteTaps;

    public BudgetPage(BudgetService budget, HouseholdSnapshotReader householdReader, InstallRoleService roles)
    {
        InitializeComponent();
        this.budget = budget;
        this.householdReader = householdReader;
        this.roles = roles;

        dogEar = new DogEar(dogEarState);
        dogEar.NoticesOpened += OnNoticesOpened;

        rootLayout.Children.Add(new GraphPaperBackground { GroundColour = Color.FromArgb(LedgerAreas.AccountsGroundHex) });
        rootLayout.Children.Add(new ScrollView { Content = bodyHost });
        rootLayout.Children.Add(overlayHost);
        Content = rootLayout;

        Wrap(LedgerArea.Budget, "Budget");
        SetCorner(dogEar); // the page's own top-right corner, flush to the binder (mockup 17)
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Release I (D09/D29): her install reads this month's categories from the household snapshot,
        // never Plaid/EF. Every other install keeps the exact ReloadAsync() path below, untouched.
        var household = await HouseholdPages.LoadAsync(roles, householdReader);
        if (household.FileIsNewer)
        {
            bodyHost.Content = HouseholdPages.NewerFileNotice();
            RefreshNotices([]);
            return;
        }

        var householdBudget = household.Snapshot?.Budget;
        if (householdBudget is not null)
        {
            RenderMonthFromHousehold(householdBudget);
            return;
        }

        await ReloadAsync();
    }

    // ---- Release I: which install this is, and her read-only render (D09/D29/D88) ----

    // D88's own gate, reproduced from the snapshot rather than re-derived: before any worksheet has
    // ever been delivered there is nothing to show, on either install.
    //
    // Her month is worked out by HouseholdBudgetView — the owner's own BudgetMonthFigures arithmetic over
    // the snapshot — so the two phones state the same total (uncategorised spending included) and the
    // same days left (measured against the snapshot's own month), and the unreadable-limit dog-ear is
    // raised from the same figures the owner's is (remediation 3.2 item 6).
    private void RenderMonthFromHousehold(HouseholdBudgetSummary summary)
    {
        if (summary.Categories.Count == 0)
        {
            bodyHost.Content = BuildEmptyState();
            RefreshNotices([]);
            return;
        }

        var view = HouseholdBudgetView.From(summary, DateOnly.FromDateTime(DateTime.Today));
        var figures = view.Figures;
        var amountsByCategory = figures.Amounts.ToDictionary(a => a.CategoryId);

        var column = new VerticalStackLayout { Spacing = 14, Padding = new Thickness(16, 0, 16, 24) };
        column.Children.Add(BuildMonthLine(summary.YearMonth, summary.TrackingOnly, figures.TotalSpent, figures.TotalAgreed, view.DaysLeft));

        foreach (var category in figures.Categories)
        {
            amountsByCategory.TryGetValue(category.Id, out var amount);
            decimal? lastMonth = figures.LastMonthByCategory is { } previous && previous.TryGetValue(category.Id, out var spent) ? spent : null;
            column.Children.Add(BuildHouseholdCategoryCard(category.Name, amount, summary.TrackingOnly, lastMonth));
        }

        if (figures.UncategorizedCount > 0)
        {
            column.Children.Add(UncategorizedCard(figures.UncategorizedTotal, tapped: null, trailingGlyph: null));
        }

        bodyHost.Content = column;

        RefreshNotices(figures.UnreadableLimits.Select(c => new UnreadableLimit(c.Name)).ToList());
    }

    // No tap: the snapshot carries no transaction-level detail (a summary, not a register — D10's own
    // DTO), so there is no post-it to open on her install.
    private static View BuildHouseholdCategoryCard(string name, BudgetCategoryAmount? amount, bool trackingOnly, decimal? lastMonth) =>
        IndexCardView.Build(name, CategoryCardRows(amount, trackingOnly, lastMonth), tapped: null, trailingGlyph: null);

    private static string CurrentYearMonth => BudgetPeriod.YearMonthOf(DateOnly.FromDateTime(DateTime.Today));

    private async Task ReloadAsync()
    {
        var yearMonth = CurrentYearMonth;
        var trackingOnly = SettingsStore.BudgetTrackingOnly;

        var existingMonth = await budget.FindMonthAsync(yearMonth, CancellationToken.None);
        var hasAnyLimit = existingMonth is not null && await budget.AnyLimitSetAsync(existingMonth.Id, CancellationToken.None);

        // D88: before any worksheet, quiet — no button, no categorised spending, regardless of
        // whether a month row or pulled transactions already exist in the background.
        if (!trackingOnly && !hasAnyLimit)
        {
            bodyHost.Content = BuildEmptyState();
            RefreshNotices([]);
            return;
        }

        var month = await budget.GetOrCreateMonthAsync(yearMonth, CancellationToken.None);
        await budget.EnsureCategoryAmountsAsync(month.Id, trackingOnly, CancellationToken.None);

        if (refreshInFlight)
        {
            // A refresh started by an earlier appearing is still running; it re-renders when it lands.
            await RenderMonthAsync(month, trackingOnly);
            return;
        }

        // Paint what is already stored first, with one status sentence saying a refresh is running
        // (DESIGN.md: loading is a sentence, never a spinner) — the Plaid pull below is a round trip per
        // institution and the page used to stay blank until the last one answered.
        refreshInFlight = true;
        refreshStatus = "Refreshing transactions…";
        try
        {
            await RenderMonthAsync(month, trackingOnly);

            // D149's "refresh on open, no cool-off" convention, applied to Budget's own cross-account
            // transaction pull (D63) rather than Accounts' balances.
            await budget.RefreshTransactionsAsync(yearMonth, CancellationToken.None);
        }
        finally
        {
            refreshInFlight = false;
            refreshStatus = null;
        }

        await RenderMonthAsync(month, trackingOnly);
    }

    private async Task RenderMonthAsync(BudgetMonth month, bool trackingOnly)
    {
        // The categories drawn, their amounts and the month line's totals all come from one place that
        // restricts to ACTIVE categories (R-G17): a removed category keeps its past rows (D55) but has no
        // card, so it must neither add to the totals nor be looked up by name.
        var figures = await budget.GetMonthFiguresAsync(month, CancellationToken.None);
        var categories = figures.Categories;
        var amountsByCategory = figures.Amounts.ToDictionary(a => a.CategoryId);
        var uncategorizedTotal = figures.UncategorizedTotal;
        var uncategorizedCount = figures.UncategorizedCount;
        var daysLeft = BudgetPeriod.DaysLeft(month.YearMonth, DateOnly.FromDateTime(DateTime.Today));

        var column = new VerticalStackLayout { Spacing = 14, Padding = new Thickness(16, 0, 16, 24) };
        column.Children.Add(BuildMonthLine(month.YearMonth, trackingOnly, figures.TotalSpent, figures.TotalAgreed, daysLeft));

        if (refreshStatus is not null)
        {
            column.Children.Add(new Label { Text = refreshStatus, Style = PaperResources.Token<Style>("StatusLine") });
        }

        foreach (var category in categories)
        {
            amountsByCategory.TryGetValue(category.Id, out var amount);
            decimal? lastMonth = figures.LastMonthByCategory is { } previous && previous.TryGetValue(category.Id, out var spent) ? spent : null;
            column.Children.Add(BuildCategoryCard(category, amount, trackingOnly, lastMonth));

            if (expandedCategoryId == category.Id)
            {
                column.Children.Add(await BuildCategoryPostItAsync(month, category, trackingOnly));
            }
        }

        if (uncategorizedCount > 0)
        {
            column.Children.Add(BuildUncategorizedCard(uncategorizedTotal));
            if (uncategorizedExpanded)
            {
                column.Children.Add(await BuildTransactionsPostItAsync(month.YearMonth, categoryId: null, "Uncategorized"));
            }
        }

        bodyHost.Content = column;

        RefreshNotices(figures.UnreadableLimits.Select(c => new UnreadableLimit(c.Name)).ToList());
    }

    private static View BuildEmptyState() => new VerticalStackLayout
    {
        Margin = new Thickness(24, 160, 24, 0),
        Children =
        {
            new Label
            {
                // D88's exact copy.
                Text = "The first budget is made on paper. When the worksheet comes back from the reMarkable, it fills in here.",
                Style = PaperResources.Token<Style>("EmptyStateLabel"),
                HorizontalTextAlignment = TextAlignment.Center,
            },
        },
    };

    // ---- the month line (mockup 7's ".bg-now") ----

    private static View BuildMonthLine(string yearMonth, bool trackingOnly, decimal spent, decimal agreed, int daysLeft)
    {
        var monthName = BudgetPeriod.DisplayMonthName(yearMonth).ToUpperInvariant();

        var kicker = new Label
        {
            Text = trackingOnly ? $"{monthName} · TRACKING ONLY" : monthName,
            Style = PaperResources.Token<Style>("FieldLabel"),
        };

        var value = new Label
        {
            FontSize = 22,
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = MainPage.Money(spent, null), FontFamily = "Mono" },
                    new Span
                    {
                        Text = trackingOnly ? " spent" : $" of {MainPage.Money(agreed, null)}",
                        FontSize = 13,
                        TextColor = PaperResources.Colour("InkSoft"),
                    },
                },
            },
        };

        var left = new VerticalStackLayout { Children = { kicker, value } };

        var daysLabel = new Label
        {
            Text = daysLeft == 1 ? "1 day left" : $"{daysLeft} days left",
            Style = PaperResources.Token<Style>("SecondaryLabel"),
            VerticalOptions = LayoutOptions.End,
        };

        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Padding = new Thickness(0, 8, 0, 4),
        };
        row.Children.Add(left);
        Grid.SetColumn(left, 0);
        row.Children.Add(daysLabel);
        Grid.SetColumn(daysLabel, 1);

        return new VerticalStackLayout
        {
            Children = { row, new BoxView { HeightRequest = 1.5, Color = PaperResources.Colour("Ink") } },
        };
    }

    // ---- category cards (mockup 7/8, D48/D116/D141/D142) ----

    private View BuildCategoryCard(BudgetCategory category, BudgetCategoryAmount? amount, bool trackingOnly, decimal? lastMonth)
    {
        var expanded = expandedCategoryId == category.Id;
        var rows = CategoryCardRows(amount, trackingOnly, lastMonth);
        return IndexCardView.Build(
            category.Name, rows,
            tapped: (_, _) => ToggleExpandedCategory(category.Id),
            trailingGlyph: expanded ? "▾" : "▸");
    }

    private static IReadOnlyList<IndexCardRow> CategoryCardRows(BudgetCategoryAmount? amount, bool trackingOnly, decimal? lastMonth)
    {
        var state = BudgetCardState.Decide(
            amount?.LimitAmount ?? 0m, amount?.ActualAmount ?? 0m, trackingOnly, amount is { LimitUnreadable: true });

        switch (state.Kind)
        {
            case BudgetCardKind.TrackingOnly:
                // D94-D97/D142 (mockup 8 frame 2): no limit, no meter, no "left", no rose — what has been
                // spent so far, and last month's spend beneath it for comparison. BudgetCardLines decides
                // whether there is a second line (a first-ever month has nothing to compare against); the
                // history post-it lists last month as well.
                return BudgetCardLines.ForTracking(state.Spent, lastMonth)
                    .Select(line => new IndexCardRow(line.Label, MainPage.Money(line.Amount, null), Muted: line.Secondary))
                    .ToList();

            case BudgetCardKind.Unreadable:
                // D49: waiting, not wrong — no meter, no rose; the second line names where the question
                // went, in the Progress role's teal (mockup 8's `.rr`) — the same hue the dog-ear holding
                // this notice wears.
                return
                [
                    new IndexCardRow("Limit not read yet", MainPage.Money(state.Spent, null)),
                    new IndexCardRow(
                        "Sent back to the reMarkable to check", string.Empty,
                        LeftInk: PaperResources.Colour("ProgressMark")),
                ];

            case BudgetCardKind.NoLimit:
                // No limit has been agreed for this category (a category added after the worksheet, or one
                // it left blank): there is nothing to be "left" of, so say what was spent and that there is
                // no limit — not "$0.00 left" against "of $0.00".
                return [new IndexCardRow($"{MainPage.Money(state.Spent, null)} spent", "No limit", Muted: true)];

            default:
                var over = state.Kind == BudgetCardKind.OverLimit;
                var leftText = over
                    ? $"Over by {MainPage.Money(Math.Abs(state.Remaining), null)}"
                    : $"{MainPage.Money(Math.Max(state.Remaining, 0m), null)} left";

                return
                [
                    new IndexCardRow(leftText, MainPage.Money(state.Spent, null), ProblemInk: over),
                    new IndexCardRow(
                        string.Empty, $"of {MainPage.Money(state.Limit, null)}", Muted: true,
                        Meter: new IndexCardMeter(state.Fraction, over)),
                ];
        }
    }

    private void ToggleExpandedCategory(Guid categoryId)
    {
        expandedCategoryId = expandedCategoryId == categoryId ? null : categoryId;
        uncategorizedExpanded = false;
        noteTaps = 0;
        _ = ReloadRenderOnlyAsync();
    }

    private View BuildUncategorizedCard(decimal total) =>
        UncategorizedCard(
            total,
            tapped: (_, _) =>
            {
                uncategorizedExpanded = !uncategorizedExpanded;
                expandedCategoryId = null;
                noteTaps = 0;
                _ = ReloadRenderOnlyAsync();
            },
            trailingGlyph: uncategorizedExpanded ? "▾" : "▸");

    // Shared with her install, which draws the same card with no tap (no transaction detail travels).
    private static View UncategorizedCard(decimal total, EventHandler<TappedEventArgs>? tapped, string? trailingGlyph) =>
        IndexCardView.Build(
            "Uncategorized", [new IndexCardRow("Spent so far", MainPage.Money(total, null))],
            tapped: tapped, trailingGlyph: trailingGlyph);

    private async Task ReloadRenderOnlyAsync()
    {
        var yearMonth = CurrentYearMonth;
        var month = await budget.FindMonthAsync(yearMonth, CancellationToken.None);
        if (month is null)
        {
            return;
        }
        await RenderMonthAsync(month, SettingsStore.BudgetTrackingOnly);
    }

    // ---- the post-it: this month's transactions, or the tracking-only history (mockup 7/8) ----

    private async Task<View> BuildCategoryPostItAsync(BudgetMonth month, BudgetCategory category, bool trackingOnly)
    {
        if (trackingOnly)
        {
            return await BuildHistoryPostItAsync(category, month.YearMonth);
        }

        return await BuildTransactionsPostItAsync(month.YearMonth, category.Id, category.Name);
    }

    private async Task<View> BuildTransactionsPostItAsync(string yearMonth, Guid? categoryId, string label)
    {
        var transactions = await budget.GetTransactionsForCategoryAsync(categoryId, yearMonth, CancellationToken.None);

        var content = new VerticalStackLayout { Spacing = 4 };
        content.Children.Add(new Label
        {
            Text = "This month",
            Style = PaperResources.Token<Style>("FieldLabel"),
            TextColor = PaperResources.Colour("InkSoft"),
        });

        var shown = NotePaging.Shown(transactions.Count, NotePaging.TransactionsInitial, NotePaging.TransactionsStep, noteTaps);
        foreach (var view in transactions.Take(shown))
        {
            content.Children.Add(BuildTransactionRow(view, categoryId));
        }

        var remaining = NotePaging.Remaining(transactions.Count, NotePaging.TransactionsInitial, NotePaging.TransactionsStep, noteTaps);
        if (remaining > 0)
        {
            // "3 more" lengthens the note in place (mockup 7) — no navigation, no dialog.
            content.Children.Add(BuildMoreLine(NotePaging.MoreLabel(remaining)));
        }

        // D63/mockup 7: "a category has no bank, so its post-it is the suite's plain yellow" —
        // PostItTint.From(null) already returns MaterialTokens.PostItHex for exactly that case.
        return PostItView.BuildManual(bankPrimaryColorHex: null, content);
    }

    private View BuildTransactionRow(BudgetTransactionView view, Guid? categoryId)
    {
        var softInk = PaperResources.Colour("InkSoft");
        var transaction = view.Transaction;

        var left = new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = transaction.MerchantName, FontSize = PaperResources.Token<double>("SecondarySize") },
                new Label
                {
                    // Pending rows are marked the way Accounts' register marks them.
                    Text = $"{CalendarDate.Short(transaction.Date)}{(transaction.Pending ? " · pending" : string.Empty)} · {view.AccountName}",
                    Style = PaperResources.Token<Style>("SecondaryLabel"),
                    TextColor = softInk,
                },
            },
        };

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Padding = new Thickness(0, 4),
        };
        grid.Children.Add(left);
        Grid.SetColumn(left, 0);

        var amount = new Label
        {
            Text = MainPage.Money(transaction.Amount, null),
            FontFamily = "Mono",
            FontSize = PaperResources.Token<double>("SecondarySize"),
            VerticalOptions = LayoutOptions.Center,
        };
        grid.Children.Add(amount);
        Grid.SetColumn(amount, 1);

        var row = new VerticalStackLayout
        {
            Children = { grid, new BoxView { HeightRequest = 1, Color = Colors.Black.WithAlpha(0.08f) } },
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => ShowRecategorizeCard(transaction, view.AccountName);
        row.GestureRecognizers.Add(tap);

        return row;
    }

    // ---- D142's tracking-only history: month by month, own-scale bars (same reasoning as Snowball's
    // small multiples: each category's bar is scaled to ITS OWN peak month, not a shared scale) ----

    private async Task<View> BuildHistoryPostItAsync(BudgetCategory category, string currentYearMonth)
    {
        var points = await budget.GetCategoryHistoryAsync(category.Id, currentYearMonth, CancellationToken.None);
        var peak = points.Count > 0 ? points.Max(p => p.Amount) : 0m;

        var content = new VerticalStackLayout { Spacing = 4 };
        content.Children.Add(new Label
        {
            Text = "Month by month",
            Style = PaperResources.Token<Style>("FieldLabel"),
            TextColor = PaperResources.Colour("InkSoft"),
        });

        var shown = NotePaging.Shown(points.Count, NotePaging.HistoryInitial, NotePaging.HistoryStep, noteTaps);
        var currentYear = DateTime.Today.Year;
        foreach (var point in points.Take(shown))
        {
            content.Children.Add(BuildHistoryRow(point, peak, currentYear));
        }
        if (NotePaging.Remaining(points.Count, NotePaging.HistoryInitial, NotePaging.HistoryStep, noteTaps) > 0)
        {
            // "Earlier months goes further back" (mockup 8): a year more per tap, in place.
            content.Children.Add(BuildMoreLine("Earlier months"));
        }

        return PostItView.BuildManual(bankPrimaryColorHex: null, content);
    }

    // Mockup 8's row (.mh): the month over a small "so far" line in a 74dp cell, a 6dp bar, and the amount
    // in a 60dp mono cell. A month from another year carries its year (HistoryRowText).
    private static View BuildHistoryRow(CategoryMonthPoint point, decimal peak, int currentYear)
    {
        var text = HistoryRowText.For(point.YearMonth, point.IsCurrentMonth, currentYear);
        var fraction = (double)Math.Clamp(peak > 0m ? point.Amount / peak : 0m, 0m, 1m);

        var monthCell = new VerticalStackLayout
        {
            WidthRequest = 74,
            VerticalOptions = LayoutOptions.Center,
            Children = { new Label { Text = text.Month, Style = PaperResources.Token<Style>("SecondaryLabel") } },
        };
        if (text.SubLine is not null)
        {
            monthCell.Children.Add(new Label
            {
                Text = text.SubLine,
                FontSize = PaperResources.Token<double>("LabelSize"),
                TextColor = PaperResources.Colour("InkSoft"),
            });
        }

        var barColumns = new Grid
        {
            HeightRequest = 6,
            VerticalOptions = LayoutOptions.Center,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(fraction, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1 - fraction, GridUnitType.Star)),
            },
        };
        var fill = new BoxView { Color = PaperResources.Colour("InkBlue") };
        Grid.SetColumn(fill, 0);
        barColumns.Children.Add(fill);

        // At least 60dp, and growing past it for a thousand-dollar month rather than wrapping the figure.
        var figure = new Label
        {
            Text = MainPage.Money(point.Amount, null),
            FontFamily = "Mono",
            FontSize = PaperResources.Token<double>("SecondarySize"),
            MinimumWidthRequest = 60,
            LineBreakMode = LineBreakMode.NoWrap,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End,
        };

        return new Grid
        {
            ColumnSpacing = 8,
            Padding = new Thickness(0, 5),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                Column(monthCell, 0), Column(barColumns, 1), Column(figure, 2),
            },
        };
    }

    // The trailing line of a post-it that has more behind it. Tapping it lengthens the note in place
    // (remediation 3.2 item 4): both of these used to be printed and dead. Its own 40dp-high target, as
    // the mockup's `.pi-more` has.
    private View BuildMoreLine(string text)
    {
        var line = new Label
        {
            Text = text,
            Style = PaperResources.Token<Style>("SecondaryLabel"),
            TextColor = PaperResources.Colour("InkSoft"),
            MinimumHeightRequest = 40,
            VerticalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            noteTaps++;
            _ = ReloadRenderOnlyAsync();
        };
        line.GestureRecognizers.Add(tap);
        return line;
    }

    private static View Column(View view, int column)
    {
        Grid.SetColumn(view, column);
        return view;
    }

    // ---- D58: recategorizing (mockup 7 frame 2) ----

    private async void ShowRecategorizeCard(Transaction transaction, string accountName)
    {
        var categories = await budget.GetActiveCategoriesAsync(CancellationToken.None);
        var options = categories
            .Select(c => new RecategorizeCategoryOption(c.Id, c.Name, c.Id == transaction.CategoryId))
            .ToList();

        var summary = $"{transaction.MerchantName} · {MainPage.Money(transaction.Amount, null)}";
        var currentName = categories.FirstOrDefault(c => c.Id == transaction.CategoryId)?.Name;
        var subtitle = currentName is null
            ? $"{accountName} · {CalendarDate.Short(transaction.Date)}"
            : $"{accountName} · {CalendarDate.Short(transaction.Date)} · now in {currentName}";

        overlayHost.Content = RecategorizeActionCard.Build(
            summary, subtitle, transaction.MerchantName, options,
            onPick: categoryId => _ = RecategorizeAsync(transaction.Id, categoryId),
            onDismiss: DismissOverlay);
        overlayHost.IsVisible = true;
    }

    private void DismissOverlay()
    {
        overlayHost.IsVisible = false;
        overlayHost.Content = null;
    }

    private async Task RecategorizeAsync(Guid transactionId, Guid newCategoryId)
    {
        var undo = await budget.RecategorizeTransactionAsync(transactionId, newCategoryId, CancellationToken.None);
        await ReloadRenderOnlyAsync();

        dogEarState.BeginUndo(
            "Moved to a new category",
            reverse: () => _ = UndoRecategorizeAsync(undo),
            DateTimeOffset.Now,
            TimeSpan.FromMilliseconds(Index.Domain.Design.DesignTokens.Motion.UndoWindowMs));
        dogEar.Refresh();
    }

    private async Task UndoRecategorizeAsync(RecategorizeUndoState undo)
    {
        await budget.RevertRecategorizeAsync(undo, CancellationToken.None);
        await ReloadRenderOnlyAsync();
    }

    // ---- D49's dog-ear notice, on the shared Waiting page (Release B/S2's mechanism) ----

    private void RefreshNotices(IReadOnlyList<UnreadableLimit> unreadableLimits)
    {
        var domainNotices = BudgetNotices.Build(unreadableLimits);

        var newKeys = new HashSet<string>();
        foreach (var notice in domainNotices)
        {
            var key = $"budget:{notice.Headline}";
            newKeys.Add(key);
            dogEarState.SetNotice(key, new DogEarNotice(notice.Headline, notice.Detail, ToHue(notice.Severity)));
        }

        foreach (var staleKey in lastNoticeKeys.Except(newKeys))
        {
            dogEarState.SetNotice(staleKey, null);
        }
        lastNoticeKeys = newKeys;

        dogEar.Refresh();
    }

    private static DogEarHue ToHue(NoticeSeverity severity) => severity switch
    {
        NoticeSeverity.Problem => DogEarHue.Problem,
        NoticeSeverity.Caution => DogEarHue.Caution,
        NoticeSeverity.Action => DogEarHue.Action,
        NoticeSeverity.Resolved => DogEarHue.Resolved,
        NoticeSeverity.Reference => DogEarHue.Reference,
        NoticeSeverity.Progress => DogEarHue.Progress,
        _ => DogEarHue.Caution,
    };

    private void OnNoticesOpened(IReadOnlyList<DogEarNotice> notices) =>
        _ = Navigation.PushAsync(new LedgerWaitingPage(LedgerArea.Budget, dogEarState));
}
