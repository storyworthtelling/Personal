using System.Globalization;
using Microsoft.Extensions.Logging;
using Index.Android.Views;
using Index.Domain.Design;
using Index.Domain.Household;
using Index.Domain.Model;
using Index.Domain.Voice;
using Index.Infrastructure.Household;
using Index.Infrastructure.Services;
using Index.Infrastructure.Snowball;
using Index.Infrastructure.Voice;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;

namespace Index.Android.Pages;

/// <summary>
/// Snowball (mockup 6): the debt payoff plan, calculated with the sheet's own formulas
/// (<see cref="SnowballEngine"/>), reusing Release B's materials exactly as Accounts does —
/// <see cref="IndexCardView"/> for each loan, <see cref="PostItView"/> for the one that's open
/// (<see cref="PostItView.BuildManual"/>, the same "no facts to fetch, just fields/content" shape a
/// hand-entered account's post-it already uses), and the graph-paper materials Accounts established.
///
/// <para>Built in code-behind, the same convention <see cref="MainPage"/> established for Release B's
/// materials, and following its own pattern exactly: both sides' static widget trees are built ONCE (in
/// the constructor) and kept alive by fields; a render only updates label text and swaps an inner
/// <see cref="ContentView"/>'s content, never rebuilds or reparents the stable chrome — which is what
/// keeps the Extra field from losing focus or its cursor position on every keystroke.</para>
/// </summary>
public partial class SnowballPage : LedgerBinderPage
{
    private readonly SnowballService snowball;
    // Release I: her install reads the debt-free date, loan list and this week's payments from the
    // published household snapshot instead of the (empty, on her phone) local database (D09/D90). See
    // HouseholdPages.LoadAsync (OnAppearing); the owner-install path below is unchanged.
    private readonly HouseholdSnapshotReader householdReader;
    // The remembered role: no page re-derives it from Drive (Phase 1.1).
    private readonly InstallRoleService roles;
    // The Snowball voice seat (remediation 3.7): one sentence above DEBT-FREE, from the weekly debt history.
    private readonly VoiceSeatComposer voiceSeats;
    private readonly ILogger<SnowballPage> logger;

    private readonly PaperSegmented sideSwitch;
    private readonly ContentView sideHost = new();
    private readonly ContentView overlayHost = new() { IsVisible = false };

    // ---- the Loans side's own stable root — built once in the constructor ----
    private readonly VerticalStackLayout loansSideRoot = new() { Spacing = LoansSideSpacing };
    // Hidden until a sentence arrives; the first thing on the Loans side, above DEBT-FREE (mockup 6).
    private readonly Label voiceLabel = VoiceSeatLine.Create();
    // Mockup 6's `.sb-now .v`: the debt-free date in mono, at the Title step (the mockup's 26 is not on the type
    // ladder). The line under it is soft ink, as the mockup's `.sb-now .w` is.
    private readonly Label debtFreeDateLabel = new()
    {
        FontFamily = "Mono",
        FontSize = PaperResources.Token<double>("TitleSize"),
    };
    private readonly Label debtFreeSubLabel = new()
    {
        Style = PaperResources.Token<Style>("SecondaryLabel"),
        TextColor = PaperResources.Colour("InkSoft"),
    };
    // D90: named so her install can hide the whole "Extra each week" group entirely — no field,
    // no reordering — matching D09's own "hidden, not shown disabled" pattern.
    private readonly Label extraHeadingLabel = new() { Text = "Extra each week", Style = PaperResources.Token<Style>("FieldLabel") };
    private readonly Entry extraEntry = new() { Keyboard = Keyboard.Numeric };
    // Mockup 6: a boxed mono field, 150 wide, the "$" drawn as a prefix rather than typed into the text.
    private FieldBox? extraBox;
    private readonly Label extraHintLabel = new()
    {
        Text = PlanText.ExtraHint,
        Style = PaperResources.Token<Style>("SecondaryLabel"),
        TextColor = PaperResources.Colour("InkSoft"),
    };
    private readonly ContentView loanCardsHost = new();

    // ---- the History side's own stable root ----
    private readonly VerticalStackLayout historySideRoot = new() { Spacing = PlanSpacing.CardGapDp };
    // Everything the History side draws when there IS history; swapped out for historyEmptyLabel when not.
    private readonly VerticalStackLayout historyChartsRoot = new() { Spacing = PlanSpacing.CardGapDp };
    private readonly Label historyEmptyLabel = new()
    {
        Text = SnowballHistoryCopy.EmptyHistory,
        Style = PaperResources.Token<Style>("EmptyStateLabel"),
        HorizontalTextAlignment = TextAlignment.Center,
        Margin = new Thickness(24, 60, 24, 0),
        IsVisible = false,
    };
    // Mockup 6 draws no caption until a bar or a panel is touched, so both start hidden (R-G24).
    private readonly Label historyCaptionLabel = new() { Style = PaperResources.Token<Style>("SecondaryLabel"), IsVisible = false };
    private readonly Label panelCaptionLabel = new() { Style = PaperResources.Token<Style>("SecondaryLabel"), IsVisible = false };
    private readonly ContentView legendHost = new();
    private readonly Label historyFootnoteLabel = new()
    {
        Style = PaperResources.Token<Style>("SecondaryLabel"),
        TextColor = PaperResources.Colour("InkSoft"),
    };
    private readonly SnowballStackedBarsChart stackedBarsChart = new();
    private readonly SnowballSmallMultiplesChart smallMultiplesChart = new();

    private Guid? expandedLoanId;
    private bool suppressExtraEntryTextChanged;

    // Non-null exactly when this appearance rendered from the household snapshot (her install) —
    // OnSideChanged reads it so switching sides stays on the same data source without re-resolving
    // the install role on every tap.
    private HouseholdSnowballSummary? lastHouseholdSnowball;

    // The whole snapshot behind lastHouseholdSnowball: her History side draws from its series (version 2).
    private HouseholdSnapshot? lastHousehold;

    // True when the household file was written by a newer Index than this build can read: both sides say
    // so, and nothing falls back to her (empty) local database.
    private bool showingNewerFile;

    public SnowballPage(SnowballService snowball, HouseholdSnapshotReader householdReader, InstallRoleService roles,
        VoiceSeatComposer voiceSeats, ILogger<SnowballPage> logger)
    {
        InitializeComponent();
        this.snowball = snowball;
        this.householdReader = householdReader;
        this.roles = roles;
        this.voiceSeats = voiceSeats;
        this.logger = logger;

        // D139: the switch remembers the last side chosen, read once here at construction.
        sideSwitch = new PaperSegmented(["Loans", "History"], SettingsStore.SnowballLastView);
        sideSwitch.Picked += OnSideChanged;

        BuildLoansSideRoot();
        BuildHistorySideRoot();
        BuildChrome();

        Wrap(LedgerArea.Snowball, "Snowball");
    }

    // Mockup 6: the graph paper runs up behind "Snowball" and its rule, not just under the well.
    protected override bool PaperBehindHeader => true;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Release I (D09/D90): her install reads the debt-free date, loan list and this week's
        // payments from the household snapshot, never Plaid/EF — and hides the extra-payment field
        // and reordering entirely, not shown-and-disabled.
        var household = await HouseholdPages.LoadAsync(roles, householdReader);
        lastHousehold = household.Snapshot;
        lastHouseholdSnowball = household.Snapshot?.Snowball;
        showingNewerFile = household.FileIsNewer;
        var isHers = lastHouseholdSnowball is not null || showingNewerFile;
        extraHeadingLabel.IsVisible = !isHers;
        if (extraBox is not null)
        {
            extraBox.IsVisible = !isHers;
        }
        extraHintLabel.IsVisible = !isHers;

        if (showingNewerFile)
        {
            sideHost.Content = SettingsStore.SnowballLastView == "History" ? historySideRoot : loansSideRoot;
            ShowNewerFile();
            voiceLabel.IsVisible = false;
            return;
        }

        if (isHers)
        {
            sideHost.Content = SettingsStore.SnowballLastView == "History" ? historySideRoot : loansSideRoot;
            if (SettingsStore.SnowballLastView == "History")
            {
                // Remediation 3.6: her History is drawn from the snapshot's weekly series (version 2) by
                // the same DrawHistory the owner's side uses — never the live service, which reads her
                // empty local database.
                RenderHistoryFromHousehold(lastHousehold!);
            }
            else
            {
                await RenderLoansFromHouseholdAsync(lastHouseholdSnowball!);
            }
            _ = ShowVoiceAsync();
            return;
        }

        // D03/D78: this week's snapshot and the one-time history import — both dedupe themselves, so
        // calling them on every appearance is safe and is what "no manual refresh anywhere" (I5) means
        // applied to Snowball's own history. The page PAINTS FIRST, from local data: the import reads the
        // owner's Drive and must never hold the page blank while it waits. SnowballOpening records the
        // week, paints, then runs the import behind the page and redraws this side in place only if rows
        // were written; whatever it does in the background is logged, never thrown at the page.
        _ = await SnowballOpening.OpenAsync(
            snowball.RecordCurrentWeekSnapshotAsync, snowball.ImportHistoryIfNeededAsync,
            paint: async () =>
            {
                sideHost.Content = SettingsStore.SnowballLastView == "History" ? historySideRoot : loansSideRoot;
                await RenderCurrentSideAsync();
                _ = ShowVoiceAsync();
            },
            repaint: () => MainThread.InvokeOnMainThreadAsync(RenderCurrentSideAsync),
            logger, CancellationToken.None);
    }

    // ---- the Snowball voice seat (remediation 3.7, R5, mockup 6): how far the debt has come down ----

    // Composed after the page has drawn and never awaited by it. The facts are a percentage and a count from
    // the weekly debt history (D114: never a dollar figure); VoiceSeatComposer asks the model at most once a
    // day per set of facts, so switching sides or coming back is not another call. Only the Loans side shows
    // the line, so nothing is composed for the History side.
    private async Task ShowVoiceAsync()
    {
        if (showingNewerFile || SettingsStore.SnowballLastView == "History")
        {
            voiceLabel.IsVisible = false;
            return;
        }

        HouseholdSnowballHistory? history;
        if (lastHouseholdSnowball is not null)
        {
            history = lastHousehold?.SnowballHistory;
        }
        else
        {
            try
            {
                history = HouseholdChartSeries.FromSnowballHistory(await snowball.GetHistoryAsync(CancellationToken.None));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reading the debt history for the line above the debt-free date did not complete.");
                voiceLabel.IsVisible = false;
                return;
            }
        }

        var facts = VoiceFactBuilder.Snowball(history);
        if (!facts.HasFacts)
        {
            voiceLabel.IsVisible = false;
            return;
        }

        var line = await voiceSeats.ComposeAsync(facts, CancellationToken.None);

        // Awaited Drive and the model: the page may have moved to the History side or a newer-file notice.
        VoiceSeatLine.Apply(voiceLabel, line,
            pageStillWantsIt: !showingNewerFile && SettingsStore.SnowballLastView != "History");
    }

    // ---- Release I: which install this is, and her read-only Loans render (D09/D90) ----

    // A file newer than this build can read (remediation 3.6): the same plain sentence on both sides.
    private void ShowNewerFile()
    {
        debtFreeDateLabel.Text = string.Empty;
        debtFreeSubLabel.Text = string.Empty;
        loanCardsHost.Content = HouseholdPages.NewerFileNotice();
        ShowHistoryMessage(HouseholdChartCopy.NewerFile);
    }

    private async Task RenderLoansFromHouseholdAsync(HouseholdSnowballSummary summary)
    {
        debtFreeDateLabel.Text = PlanText.DebtFreeDate(summary.DebtFreeDate);
        debtFreeSubLabel.Text = $"{summary.TotalWeeks} weeks · {PlanMoney.Balance(summary.TotalOwed)} to go";
        // Guarded exactly as the owner render does: the field's TextChanged re-renders the loans from
        // the LOCAL database, which is empty on her phone, and would replace this household render.
        suppressExtraEntryTextChanged = true;
        extraEntry.Text = summary.ExtraPaymentAmount.ToString("0.00", CultureInfo.InvariantCulture);
        suppressExtraEntryTextChanged = false;

        var column = new VerticalStackLayout { Spacing = PlanSpacing.CardGapDp };
        foreach (var loan in summary.LoansInDisplayOrder)
        {
            var view = ToLoanView(loan);
            column.Children.Add(BuildHouseholdLoanCard(view));
            if (expandedLoanId == view.AccountId && !view.IsPaidOff)
            {
                // The post-it itself is unchanged and reused as-is — it is pure display (facts already
                // on the card plus the once-it's-the-focus figures), nothing on it is editable either
                // way, so there is nothing "read-only" has to change about it.
                column.Children.Add(BuildLoanPostIt(view));
            }
        }
        loanCardsHost.Content = summary.LoansInDisplayOrder.Count == 0 ? BuildEmptyLoans() : column;

        await Task.CompletedTask;
    }

    // D90: the card itself, its number inert (no reorder handle) — the tap still expands/collapses the
    // post-it, wired to a household-aware toggle rather than SnowballService.
    private View BuildHouseholdLoanCard(SnowballLoanView loan) =>
        BuildLoanCard(loan, ToggleExpandedHousehold, onNumberTapped: null);

    private async void ToggleExpandedHousehold(Guid accountId)
    {
        expandedLoanId = expandedLoanId == accountId ? null : accountId;
        if (lastHouseholdSnowball is not null)
        {
            await RenderLoansFromHouseholdAsync(lastHouseholdSnowball);
        }
    }

    // A stable id derived from the loan's own name, so expandedLoanId comparisons survive a re-render
    // (the snapshot carries no account Guid of its own — D10's own DTO deliberately holds no id that
    // would only ever address a row on the OTHER phone's database).
    private static Guid StableLoanId(string name) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(name)));

    private static SnowballLoanView ToLoanView(HouseholdSnowballLoan loan) => new(
        StableLoanId(loan.Name), loan.Name, loan.DisplayNumber, loan.IsFocus, loan.IsPaidOff,
        loan.Balance, loan.Apr, loan.WeeklyPaymentNow, loan.FocusedWeeklyPayment,
        loan.WeeksBeforeFocused, loan.AmountBeforeFocused, loan.WeeksToPayoff, loan.PayoffDate);

    // ---- chrome: the segmented control (D139) + the swappable body ----

    private void BuildChrome()
    {
        var column = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(PlanSpacing.WellLeftDp, 12, PlanSpacing.WellRightDp, 24),
            Children = { sideSwitch, sideHost },
        };

        var root = new Grid();
        root.Children.Add(new ScrollView { Content = column });
        root.Children.Add(overlayHost);
        Content = root;
    }

    private async void OnSideChanged(string side)
    {
        SettingsStore.SnowballLastView = side;
        sideHost.Content = side == "History" ? historySideRoot : loansSideRoot;

        // Release I: stay on whichever data source this appearance already resolved — switching sides
        // must not re-query Plaid/EF for an install that never talks to either.
        if (showingNewerFile)
        {
            return; // both sides already say the file is newer than this build can read
        }

        if (side != "History")
        {
            _ = ShowVoiceAsync();
        }

        if (lastHouseholdSnowball is not null)
        {
            if (side == "History")
            {
                RenderHistoryFromHousehold(lastHousehold!);
            }
            else
            {
                await RenderLoansFromHouseholdAsync(lastHouseholdSnowball);
            }
            return;
        }

        await RenderCurrentSideAsync();
    }

    private Task RenderCurrentSideAsync() =>
        SettingsStore.SnowballLastView == "History" ? RenderHistoryAsync() : RenderLoansAsync();

    // ---- the Loans side (D04, D43, D123, D137) ----

    private void BuildLoansSideRoot()
    {
        extraEntry.TextChanged += (_, _) => _ = OnExtraPaymentTypedAsync(persist: false);
        extraEntry.Unfocused += (_, _) => _ = OnExtraPaymentTypedAsync(persist: true);

        loansSideRoot.Children.Add(voiceLabel);
        loansSideRoot.Children.Add(new Label { Text = "Debt-free", Style = PaperResources.Token<Style>("FieldLabel") });
        loansSideRoot.Children.Add(debtFreeDateLabel);
        loansSideRoot.Children.Add(debtFreeSubLabel);
        extraHeadingLabel.Margin = new Thickness(0, GroupGapDp, 0, 0);
        loansSideRoot.Children.Add(extraHeadingLabel);
        extraBox = new FieldBox(extraEntry, mono: true, prefix: "$") { WidthRequest = 150, HorizontalOptions = LayoutOptions.Start };
        loansSideRoot.Children.Add(extraBox);
        loansSideRoot.Children.Add(extraHintLabel);
        loansSideRoot.Children.Add(new Label
        {
            Text = "In payoff order", Style = PaperResources.Token<Style>("FieldLabel"),
            Margin = new Thickness(0, GroupGapDp, 0, 0),
        });
        // The heading sits 8dp above its first card (the cards' own gap is the stack's, between cards only).
        loanCardsHost.Margin = new Thickness(0, PlanSpacing.HeadingToCardDp - LoansSideSpacing, 0, 0);
        loansSideRoot.Children.Add(loanCardsHost);
    }

    // The Loans side stacks tight (the debt-free block is three lines of one figure); a group starts
    // PlanSpacing.GroupMarginDp below the one before, which is that margin less the stack's own gap.
    private const double LoansSideSpacing = 4;
    private const double GroupGapDp = PlanSpacing.GroupMarginDp - LoansSideSpacing;

    private async Task RenderLoansAsync(decimal? liveExtraOverride = null)
    {
        var summary = liveExtraOverride is { } typed
            ? await snowball.GetSummaryWithLiveExtraAsync(typed, CancellationToken.None)
            : await snowball.GetSummaryAsync(CancellationToken.None);

        debtFreeDateLabel.Text = PlanText.DebtFreeDate(summary.DebtFreeDate);
        debtFreeSubLabel.Text = $"{summary.TotalWeeks} weeks · {PlanMoney.Balance(summary.TotalOwed)} to go";

        // Only set from the PERSISTED value (never from a live keystroke), so typing doesn't fight
        // with itself by resetting the entry's own text and cursor position mid-edit.
        if (liveExtraOverride is null)
        {
            suppressExtraEntryTextChanged = true;
            extraEntry.Text = summary.ExtraPaymentAmount.ToString("0.00", CultureInfo.InvariantCulture);
            suppressExtraEntryTextChanged = false;
        }

        var column = new VerticalStackLayout { Spacing = PlanSpacing.CardGapDp };
        foreach (var loan in summary.LoansInDisplayOrder)
        {
            column.Children.Add(BuildLoanRow(loan));
            if (expandedLoanId == loan.AccountId && !loan.IsPaidOff)
            {
                column.Children.Add(BuildLoanPostIt(loan));
            }
        }
        loanCardsHost.Content = summary.LoansInDisplayOrder.Count == 0 ? BuildEmptyLoans() : column;
    }

    // No loans in the plan yet: say so, in the kit's empty-state voice, under the (zero) debt-free figures.
    private static View BuildEmptyLoans() => new Label
    {
        Text = SnowballHistoryCopy.EmptyLoans,
        Style = PaperResources.Token<Style>("EmptyStateLabel"),
        HorizontalTextAlignment = TextAlignment.Center,
        Margin = new Thickness(24, 24, 24, 0),
    };

    private async Task OnExtraPaymentTypedAsync(bool persist)
    {
        if (suppressExtraEntryTextChanged)
        {
            return;
        }

        // Her install shows the household's figure and has no say in it: the field is hidden there,
        // but nothing typed or focused may ever re-render from, or write to, her empty local database.
        if (lastHouseholdSnowball is not null)
        {
            return;
        }

        var amount = decimal.TryParse(extraEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;

        if (persist)
        {
            await snowball.SetExtraPaymentAmountAsync(amount, CancellationToken.None);
            await RenderLoansAsync();
        }
        else
        {
            // Mockup 6's own note: "every date recalculates as you type" — recomputed purely in
            // memory, never written to the database on a keystroke.
            await RenderLoansAsync(liveExtraOverride: amount);
        }
    }

    // D43/D123: a loan's own number is its reorder handle — MAUI has no first-class long-press
    // (MainPage's own BuildInstitutionHeading makes the identical substitution for the identical
    // reason), so the number, not the card body, opens the reorder card; the card body itself stays a
    // plain tap-to-expand. Mockup 6 draws the number INSIDE the card, left of the name.
    private View BuildLoanRow(SnowballLoanView loan) =>
        BuildLoanCard(loan, ToggleExpanded, onNumberTapped: ShowLoanActionCard);

    private View BuildLoanCard(SnowballLoanView loan, Action<Guid> toggleExpanded, Action<SnowballLoanView>? onNumberTapped)
    {
        // D12: a paid-off loan is one line, crossed out in grey by the card itself — no empty second row.
        IReadOnlyList<IndexCardRow> rows = loan.IsPaidOff
            ?
            [
                new IndexCardRow(PlanText.PaidOff(loan.PayoffDate), PlanMoney.Balance(loan.Balance)),
            ]
            :
            [
                new IndexCardRow(PlanText.PaidOff(loan.PayoffDate), PlanMoney.Balance(loan.Balance)),
                new IndexCardRow(PlanText.WeeklyNow(loan.WeeklyPaymentNow), string.Empty, Muted: true),
            ];

        var expanded = expandedLoanId == loan.AccountId;
        return IndexCardView.Build(
            loan.Name, rows,
            tapped: loan.IsPaidOff ? null : (_, _) => toggleExpanded(loan.AccountId),
            trailingGlyph: loan.IsPaidOff ? null : CardChevron.For(expanded),
            leading: BuildOrderNumber(loan, onNumberTapped),
            titleChip: loan.IsFocus ? BuildFocusChip() : null,
            crossedOut: loan.IsPaidOff);
    }

    private static View? BuildOrderNumber(SnowballLoanView loan, Action<SnowballLoanView>? onTapped)
    {
        if (loan.DisplayNumber is not { } number)
        {
            return null; // paid off: no number, no reorder handle (D12)
        }

        var badge = new Label
        {
            Text = number.ToString(CultureInfo.InvariantCulture),
            Style = PaperResources.Token<Style>("SecondaryLabel"),
            FontFamily = "Mono",
            MinimumWidthRequest = 24,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalOptions = LayoutOptions.Center,
        };
        if (onTapped is not null)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => onTapped(loan);
            badge.GestureRecognizers.Add(tap);
        }
        return badge;
    }

    // Mockup 6's "Focus" chip beside the focus loan's name, from the kit's chip styles.
    private static View BuildFocusChip() => new Border
    {
        Style = PaperResources.Token<Style>("PhaseChip"),
        VerticalOptions = LayoutOptions.Center,
        Margin = new Thickness(8, 0, 0, 0),
        Content = new Label { Text = "Focus", Style = PaperResources.Token<Style>("ChipLabelActive") },
    };

    private async void ToggleExpanded(Guid accountId)
    {
        expandedLoanId = expandedLoanId == accountId ? null : accountId;
        await RenderLoansAsync();
    }

    private View BuildLoanPostIt(SnowballLoanView loan)
    {
        var softInk = PaperResources.Colour("InkSoft");
        var content = new VerticalStackLayout { Spacing = 6 };

        var facts = new HorizontalStackLayout { Spacing = 18 };
        facts.Children.Add(FactColumn("APR", $"{loan.Apr * 100m:0.##}%", softInk));
        facts.Children.Add(FactColumn("As the focus", PlanText.AsFocus(loan.FocusedWeeklyPayment), softInk));
        content.Children.Add(facts);
        content.Children.Add(new BoxView { HeightRequest = 1, Color = softInk.WithAlpha(0.4f), Margin = new Thickness(0, 4, 0, 0) });

        var weeksBeforeText = loan.WeeksBeforeFocused == 1 ? "1 week" : $"{loan.WeeksBeforeFocused} weeks";
        var weeksToPayoffText = loan.WeeksToPayoff == 1 ? "1 week" : $"{loan.WeeksToPayoff} weeks";

        // Body ink at the Secondary step, as the mockup's `.sb-pl` is: the muted ink the SecondaryLabel style
        // carries falls under the contrast floor on a tinted post-it.
        content.Children.Add(PostItLine(new Label { Text = $"Starts in {weeksBeforeText}" }));
        content.Children.Add(PostItLine(new Label
        {
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = PlanMoney.Balance(loan.AmountBeforeFocused), FontFamily = "Mono" },
                    new Span { Text = " left when it starts" },
                },
            },
        }));
        content.Children.Add(PostItLine(new Label { Text = $"{weeksToPayoffText} once it starts" }));

        return PlanPostIt.Place(PostItView.BuildManual(loan.BankPrimaryColorHex, content), PlanSpacing.CardGapDp);
    }

    private static Label PostItLine(Label line)
    {
        line.FontSize = PaperResources.Token<double>("SecondarySize");
        line.TextColor = PaperResources.Colour("Ink");
        return line;
    }

    private static View FactColumn(string label, string value, Color softInk) => new VerticalStackLayout
    {
        Children =
        {
            new Label { Text = label, Style = PaperResources.Token<Style>("FieldLabel"), TextColor = softInk },
            new Label { Text = value, FontFamily = "Mono", FontSize = PaperResources.Token<double>("SecondarySize") },
        },
    };

    // ---- the long-press-substitute reorder card (D43, D123), duplicated from InstitutionActionCard's
    // anatomy without its Disconnect row (LoanActionCard's own remarks explain why) ----

    private void ShowLoanActionCard(SnowballLoanView loan)
    {
        var subtitle = $"#{loan.DisplayNumber} in payoff order";
        overlayHost.Content = LoanActionCard.Build(
            loan.Name, subtitle,
            canMoveUp: true, canMoveDown: true, // SnowballService clamps a move at either end
            onMoveUp: () => MoveLoan(loan.AccountId, snowball.MoveLoanUpAsync),
            onMoveDown: () => MoveLoan(loan.AccountId, snowball.MoveLoanDownAsync),
            onMoveToBottom: () => MoveLoan(loan.AccountId, snowball.MoveLoanToBottomAsync),
            onDismiss: DismissOverlay);
        overlayHost.IsVisible = true;
    }

    private void DismissOverlay()
    {
        overlayHost.IsVisible = false;
        overlayHost.Content = null;
    }

    private async void MoveLoan(Guid accountId, Func<Guid, CancellationToken, Task> move)
    {
        await move(accountId, CancellationToken.None);
        await RenderLoansAsync();
    }

    // ---- the History side (D136, D138, D139) ----

    private void BuildHistorySideRoot()
    {
        stackedBarsChart.WeekTapped += (week, total) =>
            ShowCaption(historyCaptionLabel, $"{SnowballHistoryCopy.WeekCaption(week)} · {PlanMoney.Balance(total)}");
        smallMultiplesChart.PointTapped += (name, week, balance) =>
            ShowCaption(panelCaptionLabel, $"{name}, {SnowballHistoryCopy.WeekCaption(week)} · {PlanMoney.Balance(balance)}");

        historyChartsRoot.Children.Add(PlanChartTitle.Create("Every loan, week by week", first: true));
        historyChartsRoot.Children.Add(stackedBarsChart);
        historyChartsRoot.Children.Add(legendHost);
        historyChartsRoot.Children.Add(historyCaptionLabel);
        historyChartsRoot.Children.Add(PlanChartTitle.Create("Each loan"));
        historyChartsRoot.Children.Add(smallMultiplesChart);
        historyChartsRoot.Children.Add(panelCaptionLabel);
        historyChartsRoot.Children.Add(historyFootnoteLabel);

        historySideRoot.Children.Add(historyChartsRoot);
        historySideRoot.Children.Add(historyEmptyLabel);
    }

    private static void ShowCaption(Label caption, string text)
    {
        caption.Text = text;
        caption.IsVisible = true;
    }

    private static void ClearCaption(Label caption)
    {
        caption.Text = string.Empty;
        caption.IsVisible = false;
    }

    private async Task RenderHistoryAsync()
    {
        DrawHistory(await snowball.GetHistoryAsync(CancellationToken.None));
    }

    // Her History side (remediation 3.6): the snapshot's weekly series, converted back to the owner's own
    // SnowballHistory and handed to the very same DrawHistory — so the legend, axis, footnote and touch
    // behaviour are the owner's, not a copy. A version 1 file has no series: the old sentence, only then.
    private void RenderHistoryFromHousehold(HouseholdSnapshot snapshot)
    {
        if (snapshot.SnowballHistory is not { } series)
        {
            ShowHistoryMessage(HouseholdChartCopy.SnowballNotInSummary);
            return;
        }

        DrawHistory(HouseholdChartSeries.ToSnowballHistory(series));
    }

    // One sentence instead of two blank boxes.
    private void ShowHistoryMessage(string sentence)
    {
        ClearCaption(historyCaptionLabel);
        ClearCaption(panelCaptionLabel);
        historyChartsRoot.IsVisible = false;
        historyEmptyLabel.Text = sentence;
        historyEmptyLabel.IsVisible = true;
        stackedBarsChart.SetWeeks([]);
        smallMultiplesChart.SetSeries([], []);
        legendHost.Content = null;
    }

    // The one place the History side is drawn, for both installs.
    private void DrawHistory(SnowballHistory history)
    {
        // No recorded week, or no loan to record one for.
        if (history.IsEmpty)
        {
            ShowHistoryMessage(SnowballHistoryCopy.EmptyHistory);
            return;
        }

        ClearCaption(historyCaptionLabel);
        ClearCaption(panelCaptionLabel);
        historyChartsRoot.IsVisible = true;
        historyEmptyLabel.IsVisible = false;

        var weeklyTotals = history.Weeks
            .Select((week, i) => new StackedBarWeek(week, history.LoansInFocusOrder.Select(l => l.Points[i].Balance).ToList()))
            .ToList();
        stackedBarsChart.SetWeeks(weeklyTotals);
        legendHost.Content = SnowballLegend.Build(history.LoansInFocusOrder.Select(l => l.Name).ToList());

        var smallMultiples = history.LoansInFocusOrder
            .Select(l => new SmallMultipleSeries(
                SnowballHistoryCopy.PanelName(l.Name, l.IsMortgage), l.Points.Select(p => p.Balance).ToList(), l.Peak, l.IsPaidOff))
            .ToList();
        smallMultiplesChart.SetSeries(history.Weeks, smallMultiples);
        historyFootnoteLabel.Text = SnowballHistoryCopy.Footnote(history.Weeks[0]);
    }
}
