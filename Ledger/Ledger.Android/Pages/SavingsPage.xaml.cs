using System.Globalization;
using Index.Android.Views;
using Microsoft.Extensions.Logging;
using Index.Domain.Household;
using Index.Domain.Model;
using Index.Domain.Voice;
using Index.Infrastructure.Household;
using Index.Infrastructure.Services;
using Index.Infrastructure.Voice;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.State;
using Storyworthtelling.Maui.Views;

namespace Index.Android.Pages;

/// <summary>
/// Savings (mockup 9, mockup 16, Release G): the Accounts|Growth switch mirrors Snowball
/// (<see cref="SnowballPage"/>'s own remarks explain the code-behind/stable-widget-field convention
/// this follows), the accounts side is every effectively-savings account
/// (<see cref="AccountSavings.IsEffectivelySavings"/>) as an <see cref="IndexCardView"/>, and the
/// growth side is one <see cref="SavingsGrowthChart"/> per account. D109's empty state covers both
/// sides at once: nothing to list is also nothing to chart.
///
/// <para><b>The goal post-it never loses focus while you type</b> (mockup 16, frame 2: "the card
/// behind fills in its meter, what's left and the date as the figure changes"). The whole account
/// column is rebuilt fresh at every STRUCTURAL point (initial load, expanding/collapsing a card,
/// after a persist-on-unfocus, after remove/undo) — that is safe because none of those points are
/// mid-keystroke. A keystroke itself never rebuilds the column: it only swaps the expanded account's
/// own index-card slot (which holds no input) and mutates the post-it's stored "Reaches it" label
/// directly, so the <see cref="Entry"/> the owner is typing into is never recreated or reparented.</para>
/// </summary>
public partial class SavingsPage : LedgerBinderPage
{
    private readonly SavingsService savings;
    // Release I: her install renders both sides read-only from the household snapshot instead of live
    // Plaid/EF data (D09/D29). See TryLoadHouseholdSavingsIfHersAsync; the owner-install path below is
    // unchanged.
    private readonly HouseholdSnapshotReader householdReader;
    // The remembered role: no page re-derives it from Drive (Phase 1.1).
    private readonly InstallRoleService roles;
    private readonly ILogger<SavingsPage> logger;
    // The Savings voice seat (remediation 3.7, D114): a monthly RATE, never a balance.
    private readonly VoiceSeatComposer voiceSeats;

    private readonly PaperSegmented sideSwitch;
    private readonly ContentView sideHost = new();

    // Mockup 9, frame 3: with nothing to show the page is only the sentence, centred in the well — no
    // switch, no "Saved" block. This host sits over the scroll view; ShowEmptyPage/ShowSides flip it.
    private readonly ContentView emptyHost = new() { IsVisible = false };

    private readonly DogEarState dogEarState = new();
    private readonly DogEar dogEar;

    // ---- the Accounts side's own stable root ----
    private readonly VerticalStackLayout accountsSideRoot = new() { Spacing = 10 };
    // Hidden until a sentence arrives; the first thing on the Accounts side, under the switch (mockup 9).
    private readonly Label voiceLabel = VoiceSeatLine.Create();
    private readonly Label savedTotalValueLabel = new() { FontFamily = "Mono", FontSize = PaperResources.Token<double>("HeadingSize") };
    private readonly Label savedTotalSubLabel = new() { Style = PaperResources.Token<Style>("SecondaryLabel") };
    private readonly ContentView accountsBodyHost = new();

    // ---- the Growth side's own stable root ----
    private readonly VerticalStackLayout growthSideRoot = new() { Spacing = 16 };
    private readonly ContentView growthBodyHost = new();

    private SavingsAccountsSummary? lastSummary;
    private Guid? expandedAccountId;
    private ContentView? expandedCardHost;

    // The open post-it's "save whatever is typed" step; null when no post-it is open. Returns whether a
    // new goal was actually written. Replaced every time the account column is rebuilt.
    private Func<Task<bool>>? persistOpenGoal;

    // Non-null exactly when this appearance rendered from the household snapshot (her install) —
    // OnSideChanged reads it so switching sides stays on the same data source.
    private HouseholdSavingsSummary? lastHouseholdSavings;

    // The whole snapshot behind lastHouseholdSavings: her Growth side draws from its monthly series (v2).
    private HouseholdSnapshot? lastHousehold;

    // True when the household file was written by a newer Index than this build can read.
    private bool showingNewerFile;

    public SavingsPage(SavingsService savings, HouseholdSnapshotReader householdReader, InstallRoleService roles,
        VoiceSeatComposer voiceSeats, ILogger<SavingsPage> logger)
    {
        InitializeComponent();
        this.savings = savings;
        this.householdReader = householdReader;
        this.roles = roles;
        this.logger = logger;
        this.voiceSeats = voiceSeats;

        dogEar = new DogEar(dogEarState);
        // Savings raises no notices of its own this phase (S2's Waiting page is a suite-wide sink for
        // notices Accounts already carries) — wired for the one thing this tab DOES need it for: the
        // undo-first Remove goal fold (D112/D160).
        dogEar.NoticesOpened += _ => { };

        // D139's own pattern: the switch remembers the last side chosen.
        sideSwitch = new PaperSegmented(["Accounts", "Growth"], SettingsStore.SavingsLastView);
        sideSwitch.Picked += OnSideChanged;

        BuildAccountsSideRoot();
        BuildGrowthSideRoot();
        BuildChrome();

        Wrap(LedgerArea.Savings, "Savings");
        SetCorner(dogEar);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Release I (D09/D29): her install reads accounts/goals/pace from the household snapshot,
        // never Plaid/EF. Every other install keeps the exact path below, untouched.
        var household = await HouseholdPages.LoadAsync(roles, householdReader);
        lastHousehold = household.Snapshot;
        lastHouseholdSavings = household.Snapshot?.Savings;
        showingNewerFile = household.FileIsNewer;
        if (showingNewerFile)
        {
            ShowNewerFile();
            return;
        }

        if (lastHouseholdSavings is not null)
        {
            sideHost.Content = SettingsStore.SavingsLastView == "Growth" ? growthSideRoot : accountsSideRoot;
            if (SettingsStore.SavingsLastView == "Growth")
            {
                await RenderGrowthFromHouseholdAsync(lastHouseholdSavings);
            }
            else
            {
                await RenderAccountsFromHouseholdAsync(lastHouseholdSavings);
            }
            return;
        }

        // I5: refresh on open, no manual refresh button anywhere — Savings' own monthly cadence
        // (D81), the same "dedupe-at-write, safe to call every time" shape Snowball's weekly writer
        // already established.
        await savings.RecordCurrentMonthSnapshotAsync(CancellationToken.None);

        sideHost.Content = SettingsStore.SavingsLastView == "Growth" ? growthSideRoot : accountsSideRoot;
        await RenderCurrentSideAsync();
    }

    // Mockup 16: "the goal is kept when you leave the field" — and leaving the page while the field is
    // still focused is leaving it. Unfocused alone can be missed when the page goes away first.
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await PersistOpenGoalAsync();
    }

    // ---- Release I: which install this is, and her read-only render (D09/D29) ----

    // A file newer than this build can read (remediation 3.6): the whole page is the one plain sentence,
    // the way an empty page is (Phase 2.5's rule), rather than an empty local database passing for hers.
    private void ShowNewerFile()
    {
        voiceLabel.IsVisible = false;
        persistOpenGoal = null;
        sideSwitch.IsVisible = false;
        sideHost.IsVisible = false;
        emptyHost.Content = HouseholdPages.NewerFileNotice();
        emptyHost.IsVisible = true;
    }

    private Task RenderAccountsFromHouseholdAsync(HouseholdSavingsSummary summary)
    {
        if (summary.Accounts.Count == 0)
        {
            savedTotalValueLabel.Text = string.Empty;
            savedTotalSubLabel.Text = string.Empty;
            ShowEmptyPage();
            return Task.CompletedTask;
        }

        ShowSides();
        _ = ShowVoiceAsync(VoiceFactBuilder.Savings(summary.Accounts));
        savedTotalValueLabel.Text = MainPage.Money(summary.TotalSaved, "USD");
        savedTotalSubLabel.Text = summary.Accounts.Count == 1
            ? "across 1 account"
            : $"across {summary.Accounts.Count} accounts";

        var column = new VerticalStackLayout { Spacing = 10 };
        foreach (var account in summary.Accounts)
        {
            column.Children.Add(BuildHouseholdAccountCard(account));
        }
        accountsBodyHost.Content = column;
        return Task.CompletedTask;
    }

    // No tap, no goal post-it: a read-only install has nothing to write a goal into (D09/D10's own
    // reasoning — no id here would ever address a row on the OTHER phone's database), so the goal,
    // pace and "Reaches it" the owner's post-it computes are shown directly on the card instead.
    private static View BuildHouseholdAccountCard(HouseholdSavingsAccount account)
    {
        var title = $"{account.InstitutionName} · {account.AccountName}";

        IReadOnlyList<IndexCardRow> rows;
        if (account.GoalTargetAmount is { } goal)
        {
            var reachesIt = SavingsPace.ReachesIt(account.Balance, goal, account.MonthlyPace, Today());
            var toGo = Math.Max(0m, goal - account.Balance);
            var fraction = goal > 0m ? Math.Clamp(account.Balance / goal, 0m, 1m) : 0m;

            rows =
            [
                new IndexCardRow($"{MainPage.Money(toGo, "USD")} to go", MainPage.Money(account.Balance, "USD")),
                new IndexCardRow(string.Empty, $"of {WholeDollars(goal)}", Muted: true, Meter: new IndexCardMeter(fraction, Over: false)),
                new IndexCardRow(SavingsGoalCopy.Card(account.Balance, goal, reachesIt), string.Empty, Muted: true),
            ];
        }
        else
        {
            rows =
            [
                new IndexCardRow("No goal yet", MainPage.Money(account.Balance, "USD")),
                new IndexCardRow(string.Empty, string.Empty, Muted: true),
            ];
        }

        return IndexCardView.Build(title, rows, tapped: null, trailingGlyph: null);
    }

    // Her Growth side (remediation 3.6): the snapshot's monthly series, converted back to the owner's own
    // SavingsAccountGrowth and drawn by the very same BuildGrowthColumn / SavingsGrowthChart as the
    // owner's. A version 1 file has no series: the old sentence, and only then. Nothing to list is also
    // nothing to chart, so no accounts is the empty page on both sides (Phase 2.5).
    private Task RenderGrowthFromHouseholdAsync(HouseholdSavingsSummary summary)
    {
        if (summary.Accounts.Count == 0)
        {
            ShowEmptyPage();
            return Task.CompletedTask;
        }

        var growth = lastHousehold is null
            ? null
            : HouseholdChartSeries.ToSavingsGrowth(lastHousehold, Today());
        if (growth is null)
        {
            ShowSides();
            growthBodyHost.Content = new Label
            {
                Text = HouseholdChartCopy.GrowthNotInSummary,
                Style = PaperResources.Token<Style>("SecondaryLabel"),
            };
            return Task.CompletedTask;
        }

        if (growth.Count == 0)
        {
            ShowEmptyPage();
            return Task.CompletedTask;
        }

        ShowSides();
        growthBodyHost.Content = BuildGrowthColumn(growth);
        return Task.CompletedTask;
    }

    // ---- chrome ----

    private void BuildChrome()
    {
        var column = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16, 12, 16, 24),
            Children = { sideSwitch, sideHost },
        };

        var root = new Grid();
        root.Children.Add(new GraphPaperBackground { GroundColour = Color.FromArgb(LedgerAreas.AccountsGroundHex) });
        root.Children.Add(new ScrollView { Content = column });
        root.Children.Add(emptyHost);
        Content = root;
    }

    private async void OnSideChanged(string side)
    {
        await PersistOpenGoalAsync();
        SettingsStore.SavingsLastView = side;
        sideHost.Content = side == "Growth" ? growthSideRoot : accountsSideRoot;

        // Release I: stay on whichever data source this appearance already resolved.
        if (showingNewerFile)
        {
            return; // the page is already the one sentence
        }

        if (lastHouseholdSavings is not null)
        {
            if (side == "Growth")
            {
                await RenderGrowthFromHouseholdAsync(lastHouseholdSavings);
            }
            else
            {
                await RenderAccountsFromHouseholdAsync(lastHouseholdSavings);
            }
            return;
        }

        await RenderCurrentSideAsync();
    }

    /// <summary>Mockup 9, frame 3 (D109): nothing to list is also nothing to switch between, so the
    /// whole page is the sentence. The "Saved" caption is part of the accounts side, hidden with it.</summary>
    private void ShowEmptyPage()
    {
        voiceLabel.IsVisible = false;
        persistOpenGoal = null;
        sideSwitch.IsVisible = false;
        sideHost.IsVisible = false;
        emptyHost.Content = BuildEmptyState();
        emptyHost.IsVisible = true;
    }

    private void ShowSides()
    {
        emptyHost.IsVisible = false;
        sideSwitch.IsVisible = true;
        sideHost.IsVisible = true;
    }

    private Task RenderCurrentSideAsync() =>
        SettingsStore.SavingsLastView == "Growth" ? RenderGrowthAsync() : RenderAccountsAsync();

    // ---- the Savings voice seat (remediation 3.7, R5, D114, mockup 9): the pace, never the balance ----

    // Composed after the Accounts side has drawn and never awaited by it. The facts are ONE rounded monthly
    // rate for the accounts together (VoiceFactBuilder.Savings reads nothing else off an account), so no
    // balance, goal or account name can reach the prompt. VoiceSeatComposer asks the model at most once a day
    // per rate, so a side switch or a goal edit that leaves the pace alone is not another call.
    private async Task ShowVoiceAsync(VoiceFacts facts)
    {
        if (!facts.HasFacts)
        {
            voiceLabel.IsVisible = false;
            return;
        }

        var line = await voiceSeats.ComposeAsync(facts, CancellationToken.None);

        // Awaited Drive and the model: the page may have gone empty or newer-file since, and only the
        // Accounts side carries the line.
        VoiceSeatLine.Apply(voiceLabel, line,
            pageStillWantsIt: !emptyHost.IsVisible && !showingNewerFile);
    }

    // ---- the Accounts side (D50, D56, D81, D82, D83, D109, D111, D114, D116, D131, D160) ----

    private void BuildAccountsSideRoot()
    {
        var totalColumn = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = "Saved", Style = PaperResources.Token<Style>("FieldLabel") },
                savedTotalValueLabel,
                savedTotalSubLabel,
            },
        };

        accountsSideRoot.Children.Add(voiceLabel);
        accountsSideRoot.Children.Add(totalColumn);
        accountsSideRoot.Children.Add(accountsBodyHost);
    }

    private async Task RenderAccountsAsync()
    {
        lastSummary = await savings.GetAccountsAsync(CancellationToken.None);

        if (lastSummary.Accounts.Count == 0)
        {
            savedTotalValueLabel.Text = string.Empty;
            savedTotalSubLabel.Text = string.Empty;
            expandedCardHost = null;
            ShowEmptyPage();
            return;
        }

        ShowSides();
        _ = ShowVoiceAsync(VoiceFactBuilder.Savings(lastSummary.Accounts
            .Select(account => new HouseholdSavingsAccount(
                account.InstitutionName, account.AccountName, account.Balance, account.GoalTargetAmount,
                account.MonthlyPace, account.MonthsOfHistory))
            .ToList()));
        savedTotalValueLabel.Text = MainPage.Money(lastSummary.TotalSaved, "USD");
        savedTotalSubLabel.Text = lastSummary.Accounts.Count == 1
            ? "across 1 account"
            : $"across {lastSummary.Accounts.Count} accounts";

        var column = new VerticalStackLayout { Spacing = 10 };
        expandedCardHost = null;
        persistOpenGoal = null;

        foreach (var view in lastSummary.Accounts)
        {
            var cardHost = new ContentView { Content = BuildAccountCard(view, liveGoalOverride: null) };
            column.Children.Add(cardHost);

            if (expandedAccountId == view.AccountId)
            {
                expandedCardHost = cardHost;
                column.Children.Add(BuildGoalPostIt(view));
            }
        }

        accountsBodyHost.Content = column;
    }

    // Serif italic, centred in the page (mockup 9, frame 3) — the kit's EmptyStateLabel, which Budget's
    // empty page already wears.
    private static View BuildEmptyState() => new Label
    {
        Text = "No savings accounts yet. Mark one as savings on its card in Accounts, and it grows here.",
        Style = PaperResources.Token<Style>("EmptyStateLabel"),
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Center,
        Margin = new Thickness(34, 0),
    };

    /// <summary>D131: balance, how far to its goal, a meter against the goal, and when it should get
    /// there — or, with no goal, a plain line and "Tap to set one." <paramref name="liveGoalOverride"/>
    /// is the goal figure typed into the currently-open post-it's Entry, recomputed purely in memory
    /// (never written) on every keystroke; null everywhere else, where the persisted goal is used.</summary>
    private View BuildAccountCard(SavingsAccountView view, decimal? liveGoalOverride)
    {
        var title = $"{view.InstitutionName} · {view.AccountName}";
        var expanded = expandedAccountId == view.AccountId;
        var effectiveGoal = liveGoalOverride is > 0m ? liveGoalOverride : view.GoalTargetAmount;

        IReadOnlyList<IndexCardRow> rows;
        if (effectiveGoal is { } goal)
        {
            var reachesIt = SavingsPace.ReachesIt(view.Balance, goal, view.MonthlyPace, Today());
            var toGo = Math.Max(0m, goal - view.Balance);
            var fraction = goal > 0m ? Math.Clamp(view.Balance / goal, 0m, 1m) : 0m;

            rows =
            [
                new IndexCardRow($"{MainPage.Money(toGo, "USD")} to go", MainPage.Money(view.Balance, "USD")),
                new IndexCardRow(string.Empty, $"of {WholeDollars(goal)}", Muted: true, Meter: new IndexCardMeter(fraction, Over: false)),
                new IndexCardRow(SavingsGoalCopy.Card(view.Balance, goal, reachesIt), string.Empty, Muted: true),
            ];
        }
        else
        {
            rows =
            [
                new IndexCardRow("No goal yet", MainPage.Money(view.Balance, "USD")),
                new IndexCardRow("Tap to set one", string.Empty, Muted: true),
            ];
        }

        return IndexCardView.Build(
            title, rows,
            tapped: (_, _) => ToggleExpanded(view.AccountId),
            trailingGlyph: expanded ? "▾" : "▸");
    }

    private async void ToggleExpanded(Guid accountId)
    {
        // Closing (or switching) the post-it keeps what was typed in it.
        await PersistOpenGoalAsync();
        expandedAccountId = expandedAccountId == accountId ? null : accountId;
        await RenderAccountsAsync();
    }

    /// <summary>The goal post-it (mockup 16): one field, Goal — the amount, never a date. Pace and
    /// Reaches it are worked out and shown, never entered (D82/D83); Remove goal is undo-first
    /// (D112/D160) and offered only when a goal is actually persisted, since removing what was never
    /// there is not an action to offer.</summary>
    private View BuildGoalPostIt(SavingsAccountView view)
    {
        var softInk = PaperResources.Colour("InkSoft");
        var content = new VerticalStackLayout { Spacing = 6 };

        var factsHost = new ContentView();
        content.Children.Add(factsHost);

        var goalEntry = new Entry
        {
            Text = view.GoalTargetAmount is { } persisted ? WholeDollars(persisted).TrimStart('$') : string.Empty,
            Keyboard = Keyboard.Numeric,
            FontFamily = "Mono",
        };

        void RebuildFacts(decimal? liveGoal)
        {
            var effectiveGoal = liveGoal is > 0m ? liveGoal : view.GoalTargetAmount;
            if (effectiveGoal is not { } goal)
            {
                factsHost.Content = null;
                return;
            }

            var reachesIt = SavingsPace.ReachesIt(view.Balance, goal, view.MonthlyPace, Today());
            var reachesValueLabel = new Label
            {
                Text = SavingsGoalCopy.PostIt(view.Balance, goal, reachesIt),
                FontFamily = "Mono",
                FontSize = PaperResources.Token<double>("SecondarySize"),
            };

            var facts = new HorizontalStackLayout { Spacing = 18 };
            facts.Children.Add(FactColumn("Pace", $"{MainPage.Money(view.MonthlyPace, "USD")} a month", softInk));
            facts.Children.Add(new VerticalStackLayout
            {
                Children =
                {
                    new Label { Text = "Reaches it", Style = PaperResources.Token<Style>("FieldLabel"), TextColor = softInk },
                    reachesValueLabel,
                },
            });

            var column = new VerticalStackLayout { Spacing = 4, Children = { facts } };
            if (view.MonthsOfHistory > 0)
            {
                var months = view.MonthsOfHistory;
                column.Children.Add(new Label
                {
                    Text = $"From the last {months} month{(months == 1 ? "" : "s")}' growth.",
                    Style = PaperResources.Token<Style>("SecondaryLabel"),
                    TextColor = softInk,
                });
            }
            factsHost.Content = column;
        }

        RebuildFacts(liveGoal: null);

        goalEntry.TextChanged += (_, _) =>
        {
            var typed = decimal.TryParse(goalEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value
                : (decimal?)null;

            // The card behind fills in live (mockup 16, frame 2) — its own slot, not this whole column.
            if (expandedCardHost is { } host)
            {
                host.Content = BuildAccountCard(view, liveGoalOverride: typed);
            }
            RebuildFacts(typed);
        };

        // Saves whatever is typed — from the field losing focus, the post-it closing, the side
        // switching or the page going away. A blank/unparsable field leaves whatever goal already
        // exists untouched (clearing the field is not how a goal is removed; that is the explicit
        // "Remove goal" link below), and an unchanged figure writes nothing.
        var persistedGoal = view.GoalTargetAmount;
        persistOpenGoal = async () =>
        {
            var typed = decimal.TryParse(goalEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value
                : (decimal?)null;

            if (typed is not > 0m || typed == persistedGoal)
            {
                return false;
            }

            await savings.SetGoalAsync(view.AccountId, typed.Value, CancellationToken.None);
            persistedGoal = typed;
            return true;
        };

        goalEntry.Unfocused += async (_, _) =>
        {
            if (await PersistOpenGoalAsync())
            {
                await RenderAccountsAsync();
            }
        };

        content.Children.Add(new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = "Goal", Style = PaperResources.Token<Style>("FieldLabel"), TextColor = softInk },
                goalEntry,
            },
        });

        if (view.GoalTargetAmount is not null)
        {
            var removeLabel = new Label
            {
                Text = "Remove goal",
                Style = PaperResources.Token<Style>("SecondaryLabel"),
                TextColor = PaperResources.Colour("ProblemMark"),
                Margin = new Thickness(0, 6, 0, 0),
            };
            var removeTap = new TapGestureRecognizer();
            removeTap.Tapped += (_, _) => RemoveGoal(view.AccountId, view.AccountName);
            removeLabel.GestureRecognizers.Add(removeTap);
            content.Children.Add(removeLabel);
        }

        return PostItView.BuildManual(view.BankPrimaryColorHex, content);
    }

    private static View FactColumn(string label, string value, Color softInk) => new VerticalStackLayout
    {
        Children =
        {
            new Label { Text = label, Style = PaperResources.Token<Style>("FieldLabel"), TextColor = softInk },
            new Label { Text = value, FontFamily = "Mono", FontSize = PaperResources.Token<double>("SecondarySize") },
        },
    };

    // D112/D160: undo-first — the account stays, only the goal row goes. The same shape
    // MainPage.RemoveManualAccount and BudgetPage's recategorize both already use.
    private async void RemoveGoal(Guid accountId, string accountName)
    {
        var captured = await savings.FindGoalAsync(accountId, CancellationToken.None);
        if (captured is null)
        {
            return;
        }

        await savings.RemoveGoalAsync(accountId, CancellationToken.None);
        await RenderAccountsAsync();

        dogEarState.BeginUndo(
            $"Removed the goal for {accountName}",
            reverse: () => UndoRemoveGoal(captured),
            DateTimeOffset.Now,
            TimeSpan.FromMilliseconds(Index.Domain.Design.DesignTokens.Motion.UndoWindowMs));
        dogEar.Refresh();
    }

    // The dog-ear's reverse is a plain Action, so this async-void edge awaits the restore; the restore
    // itself answers for any failure (logged with its stack) so nothing is lost in a discarded task.
    private async void UndoRemoveGoal(SavingsGoal captured) => await UndoRemoveGoalAsync(captured);

    private async Task UndoRemoveGoalAsync(SavingsGoal captured)
    {
        try
        {
            await savings.RestoreGoalAsync(captured, CancellationToken.None);
            await RenderAccountsAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not restore the removed savings goal for account {AccountId}.", captured.AccountId);
        }
    }

    /// <summary>Saves the open post-it's typed goal, if any. Never throws: a failed save is logged (the
    /// callers are event handlers with nobody above them to catch it). True when a goal was written.</summary>
    private async Task<bool> PersistOpenGoalAsync()
    {
        if (persistOpenGoal is not { } persist)
        {
            return false;
        }

        try
        {
            return await persist();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not save the savings goal typed into the open post-it.");
            return false;
        }
    }

    // ---- the Growth side (D81, D113) ----

    private void BuildGrowthSideRoot()
    {
        growthSideRoot.Children.Add(growthBodyHost);
    }

    private async Task RenderGrowthAsync()
    {
        var series = await savings.GetGrowthAsync(CancellationToken.None);

        if (series.Count == 0)
        {
            ShowEmptyPage();
            return;
        }

        ShowSides();
        growthBodyHost.Content = BuildGrowthColumn(series);
    }

    // One chart per account, titled with the account's name and (when it has a goal) the footnote that
    // explains the dotted line. The single place the Growth side is drawn, for both installs.
    private static VerticalStackLayout BuildGrowthColumn(IReadOnlyList<SavingsAccountGrowth> series)
    {
        var column = new VerticalStackLayout { Spacing = 20 };
        foreach (var account in series)
        {
            var chart = new SavingsGrowthChart();
            chart.SetSeries(account);

            var block = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label
                    {
                        Text = $"{account.InstitutionName} · {account.AccountName}",
                        Style = PaperResources.Token<Style>("FieldLabel"),
                    },
                    chart,
                },
            };

            if (account.GoalTargetAmount is not null)
            {
                block.Children.Add(new Label
                {
                    Text = "Monthly, from each month's last balance. The dotted line is the last year's pace, carried forward.",
                    Style = PaperResources.Token<Style>("SecondaryLabel"),
                });
            }

            column.Children.Add(block);
        }

        return column;
    }

    // ---- formatting ----

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);

    private static string WholeDollars(decimal value) =>
        value.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
}
