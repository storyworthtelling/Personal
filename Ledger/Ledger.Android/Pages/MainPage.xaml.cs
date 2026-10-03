using Microsoft.Extensions.Logging;
using Index.Android.Views;
using Index.Domain.Connect;
using Index.Domain.Design;
using Index.Domain.External;
using Index.Domain.Household;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Domain.Voice;
using Index.Infrastructure.Household;
using Index.Infrastructure.Services;
using Index.Infrastructure.Voice;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.State;
using Storyworthtelling.Maui.Views;
using Storyworthtelling.Voice.Secrets;

namespace Index.Android.Pages;

/// <summary>
/// Accounts (mockup 2/3/14): institutions in the owner's own order, each account an index card on
/// graph paper, a post-it on the one card that is open, first-run's three states (D107/D158), and the
/// screen's own notices behind the shared Waiting page's dog-ear (D161).
///
/// <para>Rebuilt around the shared kit's convention for anything this custom-painted: the XAML shell
/// (<c>MainPage.xaml</c>) exists only to satisfy <c>LedgerBinderShellTests</c> and
/// <c>InitializeComponent()</c>; everything drawn here is built in code, the way
/// <c>Storyworthtelling.Maui.Views.BinderTabs</c>/<c>DogEar</c> already are.</para>
/// </summary>
public partial class MainPage : LedgerBinderPage
{
    private readonly AccountService accounts;
    // The owner's category names for the post-it's transaction rows (mockup 3) — Budget's data, read here.
    private readonly BudgetService budget;
    private readonly IPlaidClient plaid;
    // The three voice seats' one composer (remediation 3.7): this page is the Accounts seat.
    private readonly VoiceSeatComposer voiceSeats;
    // The owner's debt history, the source of the Accounts seat's facts on the owner's install. Hers comes
    // from the household snapshot's weekly series instead.
    private readonly SnowballService snowball;
    private readonly ISecretsVaultStore vaultStore;
    // Release I: her install renders read-only from the published household snapshot instead of live
    // Plaid/EF data (D09/D29). See TryLoadHouseholdSnapshotIfHersAsync — the owner-install path below
    // (ReloadAsync and everything it calls) is unchanged and unaware this exists.
    private readonly HouseholdSnapshotReader householdReader;
    // The remembered role: no page re-derives it from Drive (Phase 1.1).
    private readonly InstallRoleService roles;
    // Asked whether banks are on their way back from the vault (task 2.6): while a restore is running
    // the page is in mockup 14's state 3, never "Nothing connected yet" with an Add button.
    private readonly RestoreGate restoreGate;
    // The one entry point for "refresh now" (task 2.6's seam over RefreshWorker): refresh on open asks
    // the worker through it, so there is no second refresh implementation here.
    private readonly IOwnerWorkScheduler ownerWork;
    private readonly ILogger<MainPage> logger;

    private readonly Button primaryBarButton = new();
    private readonly DogEarState dogEarState = new();
    private readonly DogEar dogEar;

    // Deferred Plaid /item/remove after a disconnect's undo window closes (see DisconnectInstitution).
    private IDispatcherTimer? pendingDisconnectReleaseTimer;
    private DisconnectedInstitution? pendingDisconnect;

    // The Accounts seat (mockup 2/11): hidden until a sentence arrives, and in first run, always.
    private readonly Label voiceLabel = VoiceSeatLine.Create();
    private readonly Label statusLabel = new();

    // Set when a connect attempt was turned back for want of Plaid keys; the next reload shows it once.
    private string? connectSentence;
    private readonly Grid rootLayout = new();
    private readonly ContentView bodyHost = new();
    private readonly ScrollView bodyScroll = new();

    // Which account's post-it is open — only one at a time (D128): opening another moves it. Only
    // SetExpanded writes it, so the "n more" tap count (below) can never outlive the card it belongs to.
    private Guid? expandedAccountId;

    // How many times "n more" has been tapped on the open post-it (mockup 3); reset whenever another card
    // opens or this one closes (SetExpanded).
    private readonly OpenNoteTaps noteTaps = new();

    // The banks this render put on the page, in the owner's order — what the action card places a bank among
    // ("first on the page", mockup 2) and dims Move up / Move down by.
    private readonly List<Guid> shownInstitutionIds = [];

    // The owner's category name for each live transaction the post-it listed, by Plaid's transaction id
    // (BudgetService.GetCategoryNamesAsync). Absent = no stored category, so the row shows none.
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> categoryNamesByPlaidAccountId = new();

    // Each rendered account card's host, by account id. A post-it edit refreshes ITS card's figures through
    // this, in place, instead of re-rendering the page — which would destroy the field being typed in and
    // drop the keyboard (R-G15). Rebuilt with every render.
    private readonly Dictionary<Guid, ContentView> cardHosts = [];

    private readonly Dictionary<string, IReadOnlyList<PlaidTransaction>> transactionsByPlaidAccountId = new();
    private readonly HashSet<string> loadingTransactionsFor = [];
    private readonly Dictionary<string, string> transactionErrorsByPlaidAccountId = new();

    // The Waiting page's own notice keys this render put up, so the next render can withdraw exactly
    // the ones that no longer apply (Guard G25 — DogEarState has no bulk-replace, so the withdrawal
    // is orchestrated here).
    private HashSet<string> lastNoticeKeys = [];

    // True while Accounts has no balance to draw (nothing connected, the vault's banks on their way back,
    // or every bank still waiting for its first figures). The voiced line stays hidden then: "with no
    // balances there's nothing for it to be about" (mockup 14). Read again after the line is composed,
    // because the compose awaits Drive and first run can begin in the meantime.
    private bool firstRunShowing;

    // Live update while banks settle (mockup 14, state 3). A bounded poll rather than an event: the
    // restore and the refresh run in other threads, and the worker persists its progress to the database
    // without telling anyone, so there is no single place an event could be raised from. The poll runs
    // only while this page is showing AND something is still settling, re-renders only when what it would
    // draw has changed (RestoreProgress.Signature), and gives up after MaxSettlePollMinutes.
    private const int MaxSettlePollMinutes = RestoreProgress.MaxPollMinutes;
    private static readonly TimeSpan SettlePollInterval = TimeSpan.FromSeconds(3);
    private IDispatcherTimer? settleTimer;
    private DateTime settlePollStartedUtc;
    private string lastSignature = string.Empty;
    private bool pollReloading;
    private bool isAppearing;

    // Refresh on open (D149). When this page asked the worker to refresh; null until it has. The status
    // line says "Refreshing balances…" from that moment until the cycle records its end (RefreshActive).
    private DateTime? refreshRequestedAtUtc;

    // True while a field in an open post-it has focus. A refresh finishing re-renders the page in place
    // (the poll, below), and a rebuild would destroy the field being typed in and drop the keyboard
    // (R-G15) — so the re-render waits until the field is left.
    private bool postItEditing;

    public MainPage(
        AccountService accounts, BudgetService budget, IPlaidClient plaid, VoiceSeatComposer voiceSeats, SnowballService snowball,
        ISecretsVaultStore vaultStore, HouseholdSnapshotReader householdReader, InstallRoleService roles,
        RestoreGate restoreGate, IOwnerWorkScheduler ownerWork, ILogger<MainPage> logger)
    {
        InitializeComponent();
        this.accounts = accounts;
        this.budget = budget;
        this.plaid = plaid;
        this.voiceSeats = voiceSeats;
        this.snowball = snowball;
        this.vaultStore = vaultStore;
        this.householdReader = householdReader;
        this.roles = roles;
        this.restoreGate = restoreGate;
        this.ownerWork = ownerWork;
        this.logger = logger;

        dogEar = new DogEar(dogEarState);
        dogEar.NoticesOpened += OnNoticesOpened;

        BuildChrome();

        primaryBarButton.Clicked += OnPrimaryBarButtonClicked;
        SetBar(primaryBarButton);

        // Accounts is the app's root, so its tab is the one lit when nothing else is.
        Wrap(LedgerArea.Accounts, "Accounts");
        SetCorner(dogEar); // the page's own top-right corner, flush to the binder (mockup 17)
    }

    // Mockup 15: the keyboard pushes the post-it up and the pinned bar hides while it is open.
    protected override bool HidesBarForKeyboard => true;

    // Mockups 2, 14 and 17 draw the graph paper running up behind "Accounts" and its rule.
    protected override bool PaperBehindHeader => true;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        isAppearing = true;
        if (Window is not null)
        {
            Window.Resumed -= OnWindowResumed;
            Window.Resumed += OnWindowResumed;
        }

        // Release I (D09/D29): her install never talks to Plaid or the local EF database at all — it
        // renders read-only from the household snapshot the owner's install published. Every other
        // install keeps the exact ReloadAsync() path below, untouched.
        var household = await HouseholdPages.LoadAsync(roles, householdReader);
        if (household.IsHers)
        {
            // Her Accounts has no primary action: the "Add an account" bar the constructor pinned
            // opens the Plaid chooser, which her install must never reach.
            SetBar();
            firstRunShowing = household.Snapshot is null || household.Snapshot.Institutions.Count == 0;
            if (household.Snapshot is not null)
            {
                RenderFromHouseholdSnapshot(household.Snapshot);
            }
            else if (household.FileIsNewer)
            {
                // A file this build cannot read is said so plainly, not shown as an empty phone.
                statusLabel.Text = string.Empty;
                bodyHost.Content = HouseholdPages.NewerFileNotice();
            }
            else
            {
                // Her side, but the household file cannot be read right now (offline, or nothing
                // published yet): the same quiet empty state as an empty snapshot — never the owner's
                // path against her empty local database, which would offer to add an account.
                statusLabel.Text = string.Empty;
                bodyHost.Content = EmptyState(
                    "Nothing connected yet.", "Nothing has come over from the other phone yet.");
            }
            _ = ShowVoiceAsync(isHers: true, household.Snapshot?.SnowballHistory);
            return;
        }

        SetBar(primaryBarButton);
        // The cached balances first: the page is never blank while Plaid is asked (D149).
        await ReloadAsync();
        StartRefreshOnOpen();
        _ = ShowVoiceAsync(isHers: false, hersHistory: null);
    }

    protected override void OnDisappearing()
    {
        isAppearing = false;
        StopSettlePolling();
        if (Window is not null)
        {
            Window.Resumed -= OnWindowResumed;
        }
        base.OnDisappearing();
    }

    // ---- refresh on open (D149, I5): every time Index is opened, no cool-off ----

    private bool RefreshActive() => RefreshOnOpen.IsActive(
        refreshRequestedAtUtc, DateTime.UtcNow, RefreshWorker.IsCycleRunning, SettingsStore.LastRunAtUtc);

    // What the status line says on the owner's install when no restore is counting banks back in: the
    // refresh sentence while one runs, the last refresh's age once it has finished.
    private string OwnerStatus() => RefreshActive() ? RefreshOnOpen.RefreshingSentence : SettingsStore.LastRunSummary;

    // Asks the worker to refresh when this install may (not hers, not before the keys are entered, not
    // while a cycle is already running — the worker's own gate refuses a second one too). The cached
    // balances are already painted when this runs; the poll below re-renders in place when it finishes.
    private void StartRefreshOnOpen()
    {
        // A restore in flight counts as a refresh already going: it schedules and runs the refresh itself
        // once the banks are back, and one started now would read a half-restored list.
        if (!RefreshOnOpen.ShouldStart(roles.Current, ownerWork.IsConfigured, RefreshActive() || restoreGate.IsRunning))
        {
            return;
        }

        refreshRequestedAtUtc = DateTime.UtcNow;
        ownerWork.RunRefreshNow();

        // Say so now rather than at the next tick, unless the page is showing something else in that spot
        // (first run's own sentences).
        if (!firstRunShowing)
        {
            statusLabel.Text = RefreshOnOpen.RefreshingSentence;
        }

        StartSettlePolling();
    }

    // Coming back to the app from the background is opening Index too, and a page's OnAppearing does not
    // fire for it. Only while Accounts is the page showing; any other page starts the refresh when Accounts
    // next appears.
    private void OnWindowResumed(object? sender, EventArgs e)
    {
        if (isAppearing)
        {
            StartRefreshOnOpen();
        }
    }

    // ---- Release I: her read-only render (D09/D10/D29/D32) ----

    // D10's status line ("Balances as of 2h ago", mockup 11) — her own counterpart to
    // SettingsStore.LastRunSummary, since her phone never itself refreshed anything.
    private void RenderFromHouseholdSnapshot(HouseholdSnapshot snapshot)
    {
        statusLabel.Text = "Balances as of " + RelativeTime.Format(snapshot.PublishedAtUtc.LocalDateTime, DateTime.Now);

        if (snapshot.Institutions.Count == 0)
        {
            bodyHost.Content = EmptyState(
                "Nothing connected yet.", "Nothing has come over from the other phone yet.");
            return;
        }

        // Mockup 11: the same rhythm as the owner's page — a bank's group starts 22 below the last, and its
        // cards are 8 below the heading and 8 apart.
        var column = new VerticalStackLayout { Spacing = 0 };
        foreach (var institution in snapshot.Institutions)
        {
            column.Children.Add(BuildHouseholdInstitutionHeading(institution));
            foreach (var account in institution.Accounts)
            {
                var card = BuildHouseholdAccountCard(account);
                card.Margin = new Thickness(0, AccountsLayout.CardGapDp, 0, 0);
                column.Children.Add(card);
            }
        }
        bodyHost.Content = column;
    }

    // D32: a stale institution still shows its own "As of …" the same way the owner's does. No tap
    // gesture — her install has no long-press action card, nothing to reorder or disconnect.
    private View BuildHouseholdInstitutionHeading(HouseholdInstitution institution)
    {
        var nameLabel = new Label { Text = institution.Name, Style = PaperResources.Token<Style>("HeadingLabel") };

        var trailing = new Label
        {
            Style = PaperResources.Token<Style>("SecondaryLabel"),
            HorizontalOptions = LayoutOptions.End,
        };
        ApplyHeadingNote(trailing, AccountCardText.HeadingNote(
            institution.IsStale, institution.AsOfUtc, institution.IsHandEntered, DateTimeOffset.Now));

        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8,
            Padding = new Thickness(0, 0, 0, 6),
        };
        row.Children.Add(nameLabel);
        Grid.SetColumn(nameLabel, 0);
        row.Children.Add(trailing);
        Grid.SetColumn(trailing, 1);

        return new VerticalStackLayout
        {
            Margin = new Thickness(0, AccountsLayout.GroupMarginDp, 0, 0),
            Children = { row, new BoxView { HeightRequest = 1.5, Color = PaperResources.Colour("Ink") } },
        };
    }

    // No post-it: the household snapshot is a summary (D10), not a transaction-level detail — this is
    // a read-only render, not a second live computation of anything her phone has no data to compute.
    private View BuildHouseholdAccountCard(HouseholdAccount account)
    {
        var maskedLabel = AccountCardText.MaskedLabel(account.Kind, account.Subtype, account.Mask);
        var balance = account.CurrentBalance is { } current ? Money(current, account.IsoCurrencyCode) : "—";

        IReadOnlyList<IndexCardRow> rows;
        if (account.Kind is AccountKind.Credit or AccountKind.Loan)
        {
            // A payment (rounded up) and a limit (nearest) are whole dollars; only the balance keeps its cents (mockup 11).
            var due = AccountCardText.DueLine(
                account.NextPaymentDueDate,
                account.NextPaymentAmount is { } amt ? AccountsMoney.Payment(amt, account.IsoCurrencyCode) : null);
            // D125: the one wording of a card's limit (AccountCardText.LimitLine).
            var limit = AccountCardText.LimitLine(
                account.CreditLimit is { } creditLimit ? AccountsMoney.Whole(creditLimit, account.IsoCurrencyCode) : null);
            rows = [new IndexCardRow(due, balance), new IndexCardRow(maskedLabel, limit, Muted: true)];
        }
        else
        {
            rows = [new IndexCardRow(maskedLabel, balance), new IndexCardRow(string.Empty, string.Empty, Muted: true)];
        }

        return IndexCardView.Build(account.Name, rows, tapped: null, trailingGlyph: null);
    }

    // ---- chrome: graph paper background, the scrollable body, the dog-ear corner ----

    private void BuildChrome()
    {
        // The graph paper is the frame's (PaperBehindHeader), so it runs up behind the header as well.
        // The well is 24 from the left and 16 from the binder (mockup 2's `.dv-well`); the status line is the
        // kit's quiet one, and the voiced line (when there is one) sits 10 below it.
        statusLabel.Style = PaperResources.Token<Style>("StatusLine");
        voiceLabel.Margin = new Thickness(2, 10, 0, 0);

        var column = new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(AccountsLayout.WellPaddingLeftDp, 0, AccountsLayout.WellPaddingRightDp, 0),
        };
        column.Children.Add(statusLabel);
        column.Children.Add(voiceLabel);
        column.Children.Add(bodyHost);

        bodyScroll.Content = column;
        rootLayout.Children.Add(bodyScroll);

        Content = rootLayout;
    }

    // ---- loading + first run (D107, D158) ----

    private async Task ReloadAsync()
    {
        statusLabel.Text = connectSentence ?? OwnerStatus();
        connectSentence = null;

        var institutions = await accounts.GetInstitutionsAsync(CancellationToken.None);
        var restoring = restoreGate.IsRunning;
        lastSignature = PageSignature(institutions, restoring);

        if (institutions.Count == 0)
        {
            firstRunShowing = true;
            await RenderFirstRunAsync();
            RefreshNotices([]);
            UpdateSettlePolling(institutions, restoring);
            return;
        }

        // Banks exist but none has a figure yet (a restore has just filled the list in): the status line
        // counts them in and Add stays away until there is something on the page.
        if (RestoreProgress.OffersAdd(institutions))
        {
            SetBar(primaryBarButton);
            UpdateBarButton();
        }
        else
        {
            SetBar();
        }

        firstRunShowing = !RestoreProgress.HasCardsToShow(institutions);
        RenderInstitutions(institutions);
        RefreshNotices(institutions);
        UpdateSettlePolling(institutions, restoring);
    }

    private async Task RenderFirstRunAsync()
    {
        var passphraseSet = !string.IsNullOrEmpty(SettingsStore.VaultPassphrase);
        // With a passphrase set the vault is opened by the restore, not probed.
        var vaultHoldsSomething = !passphraseSet && await VaultHoldsSomethingAsync();
        var screen = FirstRunPlan.Decide(passphraseSet, vaultHoldsSomething, restoreGate.IsRunning);
        // The pinned button swaps "Add an account" for the passphrase on every screen that does not
        // offer Add, except the restoring one, which has no button at all (below).
        var vaultHasUnreadBanks = screen != FirstRunScreen.Restoring && !FirstRunPlan.OffersAdd(screen);

        voiceLabel.IsVisible = false; // "with no balances there's nothing for it to be about"

        switch (screen)
        {
            case FirstRunScreen.Restoring:
                // State 3 before any bank has landed: a status sentence, not a spinner (§7). The page
                // fills in place as the restore finishes (the poll in UpdateSettlePolling).
                statusLabel.Text = "Bringing back your banks\u2026";
                bodyHost.Content = null;
                break;
            case FirstRunScreen.VaultHoldsBanks:
                statusLabel.Text = string.Empty;
                bodyHost.Content = EmptyState(
                    "Your banks are in the vault.",
                    "Enter its passphrase in Settings and they come back as they were, with no reconnecting.");
                break;
            default:
                statusLabel.Text = string.Empty;
                bodyHost.Content = EmptyState(
                    "Nothing connected yet.",
                    "Add a bank to see what\u2019s in each account and when each payment is due.");
                break;
        }

        if (screen == FirstRunScreen.Restoring)
        {
            SetBar(); // neither Add nor the passphrase: the banks are already coming back
            return;
        }

        SetBar(primaryBarButton);
        UpdateBarButton(vaultHasUnreadBanks);
    }

    // ---- live update while banks settle (mockup 14, state 3) ----

    private void UpdateSettlePolling(IReadOnlyList<Institution> institutions, bool restoring)
    {
        var needed = isAppearing && (restoring || RestoreProgress.Of(institutions).Settling || RefreshActive());
        if (!needed)
        {
            StopSettlePolling();
            return;
        }

        StartSettlePolling();
    }

    // What the page would draw, for the poll to compare: the banks' signature plus whether a refresh is
    // showing, so a refresh that finished without changing a figure still flips "Refreshing balances…" to
    // "Refreshed just now".
    private string PageSignature(IReadOnlyList<Institution> institutions, bool restoring)
    {
        return RestoreProgress.Signature(institutions, restoring) + (RefreshActive() ? "#refreshing" : string.Empty);
    }

    private void StartSettlePolling()
    {
        if (settleTimer is not null)
        {
            return; // already polling; the original start time stands, so the bound holds
        }

        settlePollStartedUtc = DateTime.UtcNow;
        settleTimer = Dispatcher.CreateTimer();
        settleTimer.Interval = SettlePollInterval;
        settleTimer.IsRepeating = true;
        settleTimer.Tick += OnSettleTick;
        settleTimer.Start();
    }

    private void StopSettlePolling()
    {
        if (settleTimer is null)
        {
            return;
        }

        settleTimer.Stop();
        settleTimer.Tick -= OnSettleTick;
        settleTimer = null;
    }

    private async void OnSettleTick(object? sender, EventArgs e)
    {
        if ((DateTime.UtcNow - settlePollStartedUtc).TotalMinutes >= MaxSettlePollMinutes)
        {
            StopSettlePolling();
            return;
        }

        if (pollReloading)
        {
            return; // the previous tick is still reading
        }

        pollReloading = true;
        try
        {
            var institutions = await accounts.GetInstitutionsAsync(CancellationToken.None);
            var restoring = restoreGate.IsRunning;
            if (PageSignature(institutions, restoring) != lastSignature)
            {
                // A field being typed in a post-it is never rebuilt under the owner's thumb: leave the
                // signature as it was and look again on the next tick, after the field is left.
                if (postItEditing)
                {
                    return;
                }

                await ReloadAsync(); // re-evaluates whether anything is still settling
            }
            else
            {
                UpdateSettlePolling(institutions, restoring);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not check whether the banks have come back; no longer checking.");
            StopSettlePolling();
        }
        finally
        {
            pollReloading = false;
        }
    }

    // Detects "a vault file exists" WITHOUT the passphrase to read it, by probing
    // ISecretsVaultStore.LoadAsync directly (bypassing SecretsVaultService.LoadAsync's own
    // short-circuit to Empty when no passphrase is set — see that method's own source). VaultEnvelope
    // .Read returns Empty only when there is no file at all, regardless of which passphrase is passed
    // in, and WrongPassphrase/Corrupt whenever a file exists that this probe cannot decrypt — which is
    // the best signal available without asking for the real passphrase. It cannot distinguish "a vault
    // with other apps' sections only" from "a vault that holds banks" (that needs decryption), so this
    // is a proxy for mockup 14's "your banks are in the vault", not a guarantee — a limitation, not
    // silently assumed away.
    // A file-exists PROXY, by design: the vault is encrypted, so its banks cannot be counted before the passphrase.
    private async Task<bool> VaultHoldsSomethingAsync()
    {
        try
        {
            var result = await vaultStore.LoadAsync(string.Empty, CancellationToken.None);
            return result.Status != SecretsVaultStatus.Empty;
        }
        catch
        {
            return false;
        }
    }

    // Mockup 14's `.dv-empty`: 180 below the header rule, a heading, and one sentence in soft ink no wider than
    // 270. Left-aligned, as drawn — the heading and the sentence share one edge.
    private static View EmptyState(string headline, string body) => new VerticalStackLayout
    {
        Margin = new Thickness(0, AccountsLayout.EmptyStateTopDp, 8, 0),
        Spacing = 8,
        Children =
        {
            new Label { Text = headline, Style = PaperResources.Token<Style>("HeadingLabel") },
            new Label
            {
                Text = body,
                Style = PaperResources.Token<Style>("SecondaryLabel"),
                TextColor = PaperResources.Colour("InkSoft"),
                HorizontalOptions = LayoutOptions.Start,
                MaximumWidthRequest = AccountsLayout.EmptyStateSentenceWidthDp,
            },
        },
    };

    // D158: the pinned bar's one button switches label — everywhere except the vault-holds-banks
    // first-run state, it reads "Add an account"; there, offering Add would double-link a bank the
    // vault already holds and spend a Trial Item (I11/D149), so the only action is the passphrase.
    private void UpdateBarButton(bool vaultHasUnreadBanks = false)
    {
        primaryBarButton.Text = vaultHasUnreadBanks ? "Enter the passphrase" : "Add an account";
    }

    private async void OnPrimaryBarButtonClicked(object? sender, EventArgs e)
    {
        // D158: the first-run "vault holds banks, no passphrase" state keeps its own destination
        // (Settings, to enter the passphrase) — offering Add there would double-link a bank the vault
        // already holds and spend a Trial Item (Guard G26, unchanged by this release).
        if (primaryBarButton.Text == "Enter the passphrase")
        {
            // Opens Settings with the passphrase field scrolled into view and focused (mockup 14).
            var setup = IPlatformApplication.Current!.Services.GetRequiredService<SetupPage>();
            setup.BeginEnterPassphrase();
            await Navigation.PushAsync(setup, animated: true);
            return;
        }

        // Mockup 4, frame 1: the chooser, over the haze, the same component
        // InstitutionActionCard already is for the reorder card (D42).
        ShowOverlay(AddAccountChooserCard.Build(
            onConnectBank: () => _ = PushPlaidLinkAsync(),
            onAddByHand: () => _ = Navigation.PushAsync(
                IPlatformApplication.Current!.Services.GetRequiredService<AddManualAccountPage>()),
            onDismiss: DismissOverlay));
    }

    // ---- the Accounts voice seat (remediation 3.7, R5): the real trend and the loans behind you ----

    // Composed after the first paint and never awaited by it: the page appears with the line hidden and the
    // sentence fills in when (and only if) there is one. The facts are percentages and counts from the
    // weekly debt history, never a figure (D114); VoiceSeatComposer asks the model at most once a day per
    // set of facts, so coming back to this page, or the refresh re-rendering it, is not another call.
    private async Task ShowVoiceAsync(bool isHers, HouseholdSnowballHistory? hersHistory)
    {
        if (firstRunShowing)
        {
            voiceLabel.IsVisible = false;
            return;
        }

        HouseholdSnowballHistory? history = hersHistory;
        if (!isHers)
        {
            try
            {
                history = HouseholdChartSeries.FromSnowballHistory(await snowball.GetHistoryAsync(CancellationToken.None));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reading the debt history for the line above the accounts did not complete.");
                voiceLabel.IsVisible = false;
                return;
            }
        }

        var facts = VoiceFactBuilder.Accounts(history);
        if (!facts.HasFacts)
        {
            voiceLabel.IsVisible = false;
            return;
        }

        var line = await voiceSeats.ComposeAsync(facts, CancellationToken.None);

        // The compose awaited Drive and the model, and first run can begin in the meantime (a disconnect
        // that empties the page): re-read it.
        VoiceSeatLine.Apply(voiceLabel, line, pageStillWantsIt: !firstRunShowing);
    }

    // ---- the institutions list (D118, D123, D124, D125, D126) ----

    private void RenderInstitutions(IReadOnlyList<Institution> institutions)
    {
        // First-run state (3), mockup 14: while some institutions have never answered their first
        // refresh, the status line counts them in rather than reading the ordinary "Refreshed …ago"
        // summary. This covers a vault restore exactly the same way it covers a brand-new connection
        // still settling — both are "nothing has come back yet for this one".
        statusLabel.Text = RestoreProgress.Of(institutions).StatusLine ?? OwnerStatus();

        // Spacing is the cards' own margins, because the post-it overlaps (a negative margin) and the groups are
        // 22 apart while the cards inside one are 8 apart: a single stack spacing cannot say all three.
        var column = new VerticalStackLayout { Spacing = 0 };
        var anyShown = false;
        var afterPostIt = false;
        cardHosts.Clear();
        shownInstitutionIds.Clear();

        foreach (var institution in institutions)
        {
            var openAccounts = institution.Accounts.Where(a => !AccountStatus.IsPaidOff(a)).ToList();
            if (openAccounts.Count == 0)
            {
                // A bank with no figures yet keeps its place under its heading with a plain line — a
                // restored bank still waiting on its first refresh, or one whose first refresh failed
                // (its fix waits behind the dog-ear). It must not vanish, or a page of pending banks
                // falls through to "Nothing connected yet".
                if (RestoreProgress.AwaitsFirstFigures(institution))
                {
                    anyShown = true;
                    shownInstitutionIds.Add(institution.Id);
                    column.Children.Add(BuildInstitutionHeading(institution, afterPostIt));
                    afterPostIt = false;
                    column.Children.Add(new Label
                    {
                        Text = RestoreProgress.PendingLine(institution),
                        Style = PaperResources.Token<Style>("SecondaryLabel"),
                        Margin = new Thickness(0, AccountsLayout.HeadingToCardDp, 0, 0),
                    });
                }

                continue; // D124: a bank with nothing open left goes with its settled accounts
            }

            anyShown = true;
            shownInstitutionIds.Add(institution.Id);
            column.Children.Add(BuildInstitutionHeading(institution, afterPostIt));
            afterPostIt = false;

            foreach (var account in openAccounts)
            {
                // 8 below the heading and 8 below the card above (less what a post-it already put there).
                var cardHost = new ContentView
                {
                    Content = BuildAccountCard(institution, account),
                    Margin = new Thickness(
                        0, afterPostIt ? AccountsLayout.GapAfterPostIt(AccountsLayout.CardGapDp) : AccountsLayout.CardGapDp, 0, 0),
                };
                afterPostIt = false;
                cardHosts[account.Id] = cardHost;
                column.Children.Add(cardHost);

                if (expandedAccountId == account.Id)
                {
                    column.Children.Add(BuildPostIt(institution, account));
                    afterPostIt = true;
                }
            }
        }

        bodyHost.Content = anyShown
            ? column
            : EmptyState("Nothing connected yet.", "Add a bank to see what\u2019s in each account and when each payment is due.");
    }

    // The heading's right-hand note, decided by AccountCardText.HeadingNote (mockup 2): the absolute
    // "As of Sep 23" in the problem colour for a broken connection, "By hand" for a hand-entered bank.
    private static void ApplyHeadingNote(Label trailing, InstitutionHeadingNote? note)
    {
        if (note is null)
        {
            return;
        }

        trailing.Text = note.Text;
        if (note.IsProblem)
        {
            trailing.TextColor = PaperResources.Colour("ProblemMark");
        }
    }

    private View BuildInstitutionHeading(Institution institution, bool afterPostIt = false)
    {
        var nameLabel = new Label { Text = institution.Name, Style = PaperResources.Token<Style>("HeadingLabel") };

        var trailing = new Label
        {
            Style = PaperResources.Token<Style>("SecondaryLabel"),
            HorizontalOptions = LayoutOptions.End,
        };
        ApplyHeadingNote(trailing, AccountCardText.HeadingNote(
            failed: institution.RefreshStatus == WorkStatus.Failed,
            lastRefreshed: institution.LastRefreshedUtc,
            handEntered: string.IsNullOrEmpty(institution.AccessToken),
            now: DateTimeOffset.Now));

        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8,
            Padding = new Thickness(0, 0, 0, 6),
        };
        row.Children.Add(nameLabel);
        Grid.SetColumn(nameLabel, 0);
        row.Children.Add(trailing);
        Grid.SetColumn(trailing, 1);

        var heading = new VerticalStackLayout
        {
            // 22 below the group before it (mockup 2's `.dv-inst`), less what a post-it above already supplied.
            Margin = new Thickness(
                0, afterPostIt ? AccountsLayout.GapAfterPostIt(AccountsLayout.GroupMarginDp) : AccountsLayout.GroupMarginDp, 0, 0),
            Children = { row, new BoxView { HeightRequest = 1.5, Color = PaperResources.Colour("Ink") } },
        };

        var tap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
        // MAUI has no first-class long-press recognizer on every platform; this app's existing
        // gestures are all plain taps (see the account row below), so the heading uses the same
        // idiom Snowball's stepper avoids drag with — reachable, not gesture-fragile: a tap on the
        // heading opens the same action card a long-press would, rather than leaving the action
        // unreachable where a platform's long-press timing differs.
        tap.Tapped += (_, _) => ShowInstitutionActionCard(institution);
        heading.GestureRecognizers.Add(tap);

        return heading;
    }

    private View BuildAccountCard(Institution institution, Account account)
    {
        var rows = AccountCardRows(account);
        var expanded = expandedAccountId == account.Id;
        return IndexCardView.Build(
            AccountTitle(account),
            rows,
            tapped: (_, _) => ToggleExpanded(institution, account),
            // A drawn chevron, not a text triangle: down while the card is closed, up once its post-it is open.
            trailingGlyph: CardChevron.For(expanded));
    }

    private static string AccountTitle(Account account) => account.Name;

    private static IReadOnlyList<IndexCardRow> AccountCardRows(Account account)
    {
        var maskedLabel = AccountCardText.MaskedLabel(account.Kind, account.Subtype, account.Mask);
        var balance = account.CurrentBalance is { } current ? Money(current, account.IsoCurrencyCode) : "—";

        if (account.Kind is AccountKind.Credit or AccountKind.Loan)
        {
            // A payment (rounded up) and a limit are whole dollars ("Due Oct 6 · $286", "of $23,000"); only the balance
            // keeps its cents (mockup 2).
            var due = AccountCardText.DueLine(
                account.NextPaymentDueDate,
                account.NextPaymentAmount is { } amt ? AccountsMoney.Payment(amt, account.IsoCurrencyCode) : null);
            // D125: the same "of $limit" for a hand-entered card as for a Plaid one, whatever the account's source.
            var limit = AccountCardText.LimitLine(
                account.CreditLimit is { } creditLimit ? AccountsMoney.Whole(creditLimit, account.IsoCurrencyCode) : null);

            return
            [
                new IndexCardRow(due, balance),
                new IndexCardRow(maskedLabel, limit, Muted: true),
            ];
        }

        return
        [
            new IndexCardRow(maskedLabel, balance),
            new IndexCardRow(string.Empty, string.Empty, Muted: true),
        ];
    }

    // ---- the post-it (D127, D128) ----

    private View BuildPostIt(Institution institution, Account account)
    {
        // D111/D159: every edit lives on the account's own post-it. A hand-entered account's post-it is
        // an entirely different shape (its own fields, no APR/Available facts row, no transactions —
        // there is nothing at the other end of a refresh to have any), so it branches out first.
        if (account.Source == AccountSource.Manual)
        {
            return BuildManualPostIt(institution, account);
        }

        // Null when Plaid reports no APR (a savings account): the post-it then draws no APR column.
        var aprText = account.Apr is { } apr ? $"{apr:0.##}%" : null;
        var availableText = account.AvailableBalance is { } available ? Money(available, account.IsoCurrencyCode) : "—";
        var footer = BuildSavingsSwitchFooter(institution, account);

        if (transactionErrorsByPlaidAccountId.TryGetValue(account.AccountId, out var error))
        {
            return PostItView.Build(institution.PrimaryColorHex, aprText, availableText,
                [new PostItTransactionRow(error, string.Empty, string.Empty)], moreCount: 0, footer: footer);
        }

        if (loadingTransactionsFor.Contains(account.AccountId))
        {
            return PostItView.Build(institution.PrimaryColorHex, aprText, availableText,
                [new PostItTransactionRow("Loading recent transactions…", string.Empty, string.Empty)], moreCount: 0, footer: footer);
        }

        if (!transactionsByPlaidAccountId.TryGetValue(account.AccountId, out var transactions))
        {
            // First render after expanding: kick off the fetch, show a quiet loading row now.
            loadingTransactionsFor.Add(account.AccountId);
            _ = LoadTransactionsAsync(institution, account);
            return PostItView.Build(institution.PrimaryColorHex, aprText, availableText,
                [new PostItTransactionRow("Loading recent transactions…", string.Empty, string.Empty)], moreCount: 0, footer: footer);
        }

        // Five show, and "n more" lengthens the note in place by ten a tap (mockup 3, NotePaging); the tap
        // count belongs to this open card and starts again when another opens.
        var taps = noteTaps.For(account.Id);
        var showing = NotePaging.Shown(transactions.Count, NotePaging.AccountTransactionsInitial, NotePaging.TransactionsStep, taps);
        var categoryNames = categoryNamesByPlaidAccountId.GetValueOrDefault(account.AccountId);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var shown = transactions.Take(showing)
            .Select(t => new PostItTransactionRow(
                string.IsNullOrEmpty(t.MerchantName) ? t.Name : t.MerchantName!,
                TransactionRowText.WhenAndCategory(t.Date, today, t.Pending, categoryNames?.GetValueOrDefault(t.TransactionId)),
                Money((decimal)t.Amount, t.IsoCurrencyCode)))
            .ToList();

        return PostItView.Build(institution.PrimaryColorHex, aprText, availableText, shown,
            moreCount: NotePaging.Remaining(transactions.Count, NotePaging.AccountTransactionsInitial, NotePaging.TransactionsStep, taps),
            footer: footer,
            onMore: () => ShowMoreTransactions(account));
    }

    // ---- D56/D159: the Savings switch, the one thing a CONNECTED account's post-it can edit ----

    private View BuildSavingsSwitchFooter(Institution institution, Account account)
    {
        var plaidSays = AccountSavings.PlaidSaysSavings(account.Subtype);
        var effective = AccountSavings.IsEffectivelySavings(account);

        var toggle = new Switch
        {
            IsToggled = effective,
            // D116: Index draws no Positive-role hue anywhere — the identity green IS the sanctioned
            // substitute for it on Index's own pages, so the switch's "on" reads in the Accounts tab's
            // own mark colour rather than a role token.
            OnColor = LedgerAreas.All[(int)LedgerArea.Accounts].Hue,
            ThumbColor = PaperResources.Colour("PaperBright"),
            VerticalOptions = LayoutOptions.Center,
        };
        toggle.Toggled += async (_, args) =>
        {
            await accounts.SetSavingsOverrideAsync(account.Id, args.Value, CancellationToken.None);
            var institutions = await accounts.GetInstitutionsAsync(CancellationToken.None);
            RenderInstitutions(institutions);
        };

        var text = new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = "Savings", Style = PaperResources.Token<Style>("CardRowTitle") },
                // The line is mockup 15's ("Plaid calls it savings. It\u2019s on the Savings tab."). It was
                // only ever drawn for an account Plaid DOES call savings; the opposite case draws no line
                // rather than an invented one. Soft ink: muted falls below the floor on a tinted post-it.
                new Label
                {
                    Text = plaidSays ? "Plaid calls it savings. It\u2019s on the Savings tab." : string.Empty,
                    IsVisible = plaidSays,
                    Style = PaperResources.Token<Style>("SecondaryLabel"),
                    TextColor = PaperResources.Colour("InkSoft"),
                },
            },
        };

        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Padding = new Thickness(0, 8, 0, 0),
        };
        row.Children.Add(text);
        Grid.SetColumn(text, 0);
        row.Children.Add(toggle);
        Grid.SetColumn(toggle, 1);

        return new VerticalStackLayout
        {
            Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                new BoxView { HeightRequest = 1, Color = Color.FromArgb(PostItTint.Edge(PostItTint.From(institution.PrimaryColorHex))) },
                row,
            },
        };
    }

    // ---- D159: a hand-entered account's post-it — its own fields, saved on leaving each ----

    // One of the post-it's money/percent boxes: how its text is read, what is stored for it now, and the
    // patch that carries a new value for it and nothing else.
    private sealed class ManualFigure(
        FieldBox box, FigureKind kind, bool allowNegative,
        Func<Account, decimal?> stored, Func<decimal, ManualAccountPatch> patch)
    {
        public FieldBox Box { get; } = box;
        public FigureKind Kind { get; } = kind;
        public bool AllowNegative { get; } = allowNegative;
        public Func<Account, decimal?> Stored { get; } = stored;
        public Func<decimal, ManualAccountPatch> Patch { get; } = patch;
        public BlurThenLive Live { get; } = new();
    }

    private View BuildManualPostIt(Institution institution, Account account)
    {
        var softInk = PaperResources.Colour("InkSoft");
        var column = new VerticalStackLayout { Spacing = 6 };

        // Kind decides which fields the post-it offers, as it did the Add form (mockup 4): a card also has its
        // limit, a bank account has no payment to keep.
        var shown = ManualAccountForm.FieldsFor(account.Kind);

        // What is stored right now. Replaced by each save's result, so "did this change?" is always asked
        // of the truth and not of the account as it was when the post-it opened.
        var current = account;

        var snowballNote = new Label
        {
            Text = ManualAccountEditing.NotInSnowballSentence,
            Style = PaperResources.Token<Style>("SecondaryLabel"),
            TextColor = softInk, // on the yellow post-it, soft ink (mockup 3's measurement), never muted
        };
        var saveFailed = new Label
        {
            Text = ManualAccountEditing.SaveFailedSentence,
            Style = PaperResources.Token<Style>("SecondaryLabel"),
            TextColor = PaperResources.Colour("ProblemMark"),
            IsVisible = false,
        };
        column.Children.Add(snowballNote);
        column.Children.Add(saveFailed);

        FieldBox NewBox(decimal? stored, FigureKind kind)
        {
            // Mockup 15: a boxed mono field that draws its own "$" before a sum and "%" after a rate, so the
            // entry holds the bare number.
            var entry = new Entry
            {
                Text = stored is { } value ? FigureParser.FormatBare(value, kind) : string.Empty,
                Keyboard = Keyboard.Numeric,
            };
            KeepVisibleWhenFocused(bodyScroll, entry);
            entry.Focused += (_, _) => postItEditing = true;
            entry.Unfocused += (_, _) => postItEditing = false;
            return new FieldBox(entry, mono: true, prefix: FigureAffix.PrefixOf(kind), suffix: FigureAffix.SuffixOf(kind));
        }

        var balance = new ManualFigure(
            NewBox(current.CurrentBalance, FigureKind.Money), FigureKind.Money, allowNegative: true,
            a => a.CurrentBalance, v => new ManualAccountPatch { Balance = v });
        var creditLimit = new ManualFigure(
            NewBox(current.CreditLimit, FigureKind.Money), FigureKind.Money, allowNegative: false,
            a => a.CreditLimit, v => new ManualAccountPatch { CreditLimit = v });
        var apr = new ManualFigure(
            NewBox(current.Apr, FigureKind.Percent), FigureKind.Percent, allowNegative: false,
            a => a.Apr, v => new ManualAccountPatch { Apr = v });
        var payment = new ManualFigure(
            NewBox(current.NextPaymentAmount, FigureKind.Money), FigureKind.Money, allowNegative: false,
            a => a.NextPaymentAmount, v => new ManualAccountPatch { Payment = v });

        var everySegmented = new Storyworthtelling.Maui.Views.PaperSegmented(
            ["Week", "Month"], current.PaymentCadence?.ToString() ?? "Month");
        // An unset Every is drawn as Month (the segmented control always has one picked) but is NOT written
        // as Month: only a tap that changes the pick saves a cadence.
        var nextDue = new DayChooser(current.NextPaymentDueDate is { } storedDue ? CalendarDate.Of(storedDue) : null);

        var updatedLabel = new Label { Style = PaperResources.Token<Style>("SecondaryLabel"), TextColor = softInk };

        // Mockup 15: "Updated Sep 12" for a figure typed in, "From Post, Sep 12" for one Post read off a statement.
        string? UpdatedText() => BalanceDisplay.UpdatedLine(current, DateTimeOffset.Now);

        bool Present(ManualFigure f) =>
            FigureParser.Parse(f.Box.Entry.Text, f.Kind, f.AllowNegative).Status == FigureStatus.Valid
            || f.Stored(current) is not null;

        // The required tint and the first line stay until BOTH APR and Payment are there, and clear live.
        void RefreshRequired()
        {
            var needed = shown.HasFlag(ManualField.Apr)
                && ManualAccountEditing.RequiredTintApplies(Present(apr), Present(payment));
            apr.Box.SetRequiredTint(needed);
            payment.Box.SetRequiredTint(needed);
            snowballNote.IsVisible = needed;
        }

        // Updates what the post-it's save changed, in place: the card behind it, the "Updated" line, the
        // required state. It never rebuilds the post-it or the page, so the field being typed in keeps its
        // focus and the keyboard stays up (R-G15).
        void RefreshInPlace()
        {
            if (cardHosts.TryGetValue(account.Id, out var host))
            {
                host.Content = BuildAccountCard(institution, current);
            }

            var updated = UpdatedText();
            updatedLabel.Text = updated ?? string.Empty;
            updatedLabel.IsVisible = updated is not null;
            RefreshRequired();
        }

        async Task<bool> SaveAsync(ManualAccountPatch patch)
        {
            try
            {
                var saved = await accounts.UpdateManualAccountAsync(account.Id, patch, CancellationToken.None);
                if (saved is null)
                {
                    return false; // removed while the post-it was open: nothing to update
                }

                current = saved;
                saveFailed.IsVisible = false;
                RefreshInPlace();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not save an edit to the hand-entered account {AccountId}.", account.Id);
                saveFailed.IsVisible = true;
                return false;
            }

            // The Waiting page's notices may have changed with the figures (a stale balance is now fresh);
            // refreshing them touches the dog-ear, never the body.
            try
            {
                RefreshNotices(await accounts.GetInstitutionsAsync(CancellationToken.None));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Saved an edit to {AccountId} but could not refresh the Waiting page's notices.", account.Id);
            }

            return true;
        }

        // The box shows what is stored: after a save, after an emptied box, after a figure typed in another
        // shape ("$1,200" for 1200.00). A box left unreadable is NOT restored — the owner is mid-fix.
        void ShowStored(ManualFigure f)
        {
            f.Box.Entry.Text = f.Stored(current) is { } value ? FigureParser.FormatBare(value, f.Kind) : string.Empty;
        }

        string? ErrorOf(ManualFigure f) =>
            ManualAccountEditing.FigureError(f.Box.Entry.Text, f.Kind, required: false, f.AllowNegative);

        async Task LeaveAsync(ManualFigure f)
        {
            var text = f.Box.Entry.Text;
            f.Box.ShowError(f.Live.Blur(ErrorOf(f)));

            var edit = FigureEdit.Decide(text, f.Kind, f.Stored(current), f.AllowNegative);
            switch (edit.Outcome)
            {
                case FigureEditOutcome.Invalid:
                    return; // the rose is showing; the stored value is exactly as it was
                case FigureEditOutcome.Write:
                    if (await SaveAsync(f.Patch(edit.Value)))
                    {
                        ShowStored(f);
                    }
                    break;
                default:
                    ShowStored(f); // empty keeps what is stored (mockup 15 draws no way to clear a figure)
                    break;
            }

            RefreshRequired();
        }

        var figures = new List<ManualFigure> { balance };
        if (shown.HasFlag(ManualField.CreditLimit))
        {
            figures.Add(creditLimit);
        }

        if (shown.HasFlag(ManualField.Apr))
        {
            figures.Add(apr);
            figures.Add(payment);
        }

        foreach (var figure in figures)
        {
            var f = figure;
            f.Box.Entry.Unfocused += async (_, _) => await LeaveAsync(f);
            f.Box.Entry.TextChanged += (_, _) =>
            {
                f.Box.ShowError(f.Live.Typed(ErrorOf(f)));
                RefreshRequired();
            };
        }

        everySegmented.Picked += value =>
        {
            if (Enum.TryParse<Index.Domain.Model.PaymentCadence>(value, out var cadence))
            {
                _ = SaveAsync(new ManualAccountPatch { Cadence = cadence });
            }
        };
        nextDue.DayChosen += day => _ = SaveAsync(ManualAccountPatch.OfNextDue(day));

        updatedLabel.Text = UpdatedText() ?? string.Empty;
        updatedLabel.IsVisible = UpdatedText() is not null;

        column.Children.Add(ManualFieldRow("Balance", updatedLabel, balance.Box));
        if (shown.HasFlag(ManualField.CreditLimit))
        {
            column.Children.Add(ManualFieldRow("Credit limit", null, creditLimit.Box));
        }

        if (shown.HasFlag(ManualField.Apr))
        {
            column.Children.Add(new HorizontalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new VerticalStackLayout { WidthRequest = 110, Children = { ManualFieldRow("APR", null, apr.Box) } },
                    new VerticalStackLayout { Children = { ManualFieldRow("Payment", null, payment.Box) } },
                },
            });
        }

        if (shown.HasFlag(ManualField.Every))
        {
            column.Children.Add(ManualFieldRow("Every", null, everySegmented));
        }

        if (shown.HasFlag(ManualField.NextDue))
        {
            column.Children.Add(ManualFieldRow("Next due", null, nextDue));
        }

        RefreshRequired();

        // D112, D159: undo-first Remove, at the foot in the problem rose — matching how removing a
        // Plaid institution already works (DisconnectInstitution). Mockup 15 sets a rule above it, and it is
        // a 48dp target, not a line of text.
        var removeRule = new BoxView
        {
            HeightRequest = 1,
            Color = Color.FromArgb(PostItTint.Edge(PostItTint.From(institution.PrimaryColorHex))),
            Margin = new Thickness(0, 10, 0, 0),
        };
        var removeLabel = new Label
        {
            Text = "Remove this account",
            Style = PaperResources.Token<Style>("FieldLabel"),
            TextColor = PaperResources.Colour("ProblemMark"),
            MinimumHeightRequest = AccountsLayout.RemoveHitDp,
            VerticalTextAlignment = TextAlignment.Center,
        };
        var removeTap = new TapGestureRecognizer();
        removeTap.Tapped += (_, _) => RemoveManualAccount(account);
        removeLabel.GestureRecognizers.Add(removeTap);
        column.Children.Add(removeRule);
        column.Children.Add(removeLabel);

        return PostItView.BuildManual(institution.PrimaryColorHex, column);
    }

    // A field's caption on the post-it: its label on the left and, where the mockup draws one ("Updated Aug 25"),
    // a hint on the right of the same line (mockup 15). Both in soft ink: muted falls below the floor on a tint.
    private static View ManualFieldRow(string label, Label? hint, View control)
    {
        var labelRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 6,
        };
        labelRow.Children.Add(new Label
        {
            Text = label,
            Style = PaperResources.Token<Style>("FieldLabel"),
            TextColor = PaperResources.Colour("InkSoft"),
        });
        if (hint is not null)
        {
            hint.HorizontalOptions = LayoutOptions.End;
            hint.HorizontalTextAlignment = TextAlignment.End;
            labelRow.Children.Add(hint);
            Grid.SetColumn(hint, 1);
        }

        return new VerticalStackLayout { Spacing = 4, Children = { labelRow, control } };
    }

    private async void RemoveManualAccount(Account account)
    {
        var captured = await accounts.FindAccountAsync(account.Id, CancellationToken.None);
        if (captured is null)
        {
            return;
        }

        if (expandedAccountId == account.Id)
        {
            SetExpanded(null);
        }

        await accounts.RemoveAccountAsync(account.Id, CancellationToken.None);
        await ReloadAsync();

        dogEarState.BeginUndo(
            $"Removed {account.Name}",
            reverse: () => _ = UndoRemoveAccountAsync(captured),
            DateTimeOffset.Now,
            TimeSpan.FromMilliseconds(Index.Domain.Design.DesignTokens.Motion.UndoWindowMs));
        dogEar.Refresh();
    }

    private async Task UndoRemoveAccountAsync(Account captured)
    {
        await accounts.RestoreAccountAsync(captured, CancellationToken.None);
        await ReloadAsync();
    }

    private void SetExpanded(Guid? accountId)
    {
        expandedAccountId = accountId;
        noteTaps.Opened(accountId);
    }

    // "17 more" (mockup 3): lengthens the open post-it in place — the next ten rows, no navigation.
    private async void ShowMoreTransactions(Account account)
    {
        noteTaps.Tapped(account.Id);
        var institutions = await accounts.GetInstitutionsAsync(CancellationToken.None);
        RenderInstitutions(institutions);
    }

    private async void ToggleExpanded(Institution institution, Account account)
    {
        // Opening another moves the post-it (D128): only one at a time, and its "n more" starts again.
        SetExpanded(expandedAccountId == account.Id ? null : account.Id);

        var institutions = await accounts.GetInstitutionsAsync(CancellationToken.None);
        RenderInstitutions(institutions);
    }

    private async Task LoadTransactionsAsync(Institution institution, Account account)
    {
        try
        {
            var end = DateOnly.FromDateTime(DateTime.Today);
            var start = end.AddDays(-NotePaging.TransactionWindowDays);
            var transactions = await plaid.GetTransactionsAsync(
                institution.AccessToken, [account.AccountId], start, end,
                count: NotePaging.TransactionFetchCap, CancellationToken.None);

            var ordered = transactions.OrderByDescending(t => t.Date).ToList();
            transactionsByPlaidAccountId[account.AccountId] = ordered;

            // The owner's category for each row comes from what Budget stored against the same transaction.
            // A failure to read it costs the category only: the rows still show, with none.
            try
            {
                categoryNamesByPlaidAccountId[account.AccountId] = await budget.GetCategoryNamesAsync(ordered, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not look up the owner's categories for {AccountId}'s transactions; showing none.", account.Id);
            }
        }
        catch (Exception ex)
        {
            transactionErrorsByPlaidAccountId[account.AccountId] = ex.Message.Contains("NOT_READY", StringComparison.OrdinalIgnoreCase)
                ? "Transactions aren\u2019t ready yet — try again in a minute."
                : "Couldn\u2019t load transactions.";
        }
        finally
        {
            loadingTransactionsFor.Remove(account.AccountId);
        }

        if (expandedAccountId == account.Id)
        {
            var institutions = await accounts.GetInstitutionsAsync(CancellationToken.None);
            RenderInstitutions(institutions);
        }
    }

    // ---- the long-press action card (mockup 2, D123) ----

    private void ShowInstitutionActionCard(Institution institution)
    {
        var openCount = institution.Accounts.Count(a => !AccountStatus.IsPaidOff(a));

        // Mockup 2: "4 accounts · first on the page", and Move up dimmed on the first bank, Move down on the
        // last. The place is among the banks the page is SHOWING (D124 removes a settled one), in the owner's
        // order — InstitutionPlacement decides it, so the dimmed row can never be a row that does nothing.
        var placement = InstitutionPlacement.Of(shownInstitutionIds, institution.Id);
        ShowOverlay(InstitutionActionCard.Build(
            institution.Name,
            placement.Subtitle(openCount),
            canMoveUp: placement.CanMoveUp,
            canMoveDown: placement.CanMoveDown,
            canMoveToBottom: placement.CanMoveToBottom,
            onMoveUp: () => MoveInstitution(institution.Id, accounts.MoveInstitutionUpAsync),
            onMoveDown: () => MoveInstitution(institution.Id, accounts.MoveInstitutionDownAsync),
            onMoveToBottom: () => MoveInstitution(institution.Id, accounts.MoveInstitutionToBottomAsync),
            onDisconnect: () => DisconnectInstitution(institution),
            onDismiss: DismissOverlay));
    }

    private void DismissOverlay() => HideOverlay();

    private async void MoveInstitution(Guid institutionId, Func<Guid, CancellationToken, Task> move)
    {
        await move(institutionId, CancellationToken.None);
        await ReloadAsync();
    }

    // Undo-first (DESIGN.md §7/§13): a disconnect removes the institution right away and folds the
    // dog-ear's corner with a reverse action, rather than a confirm dialog. The institution AND every
    // account cascade-deleted with it are captured first (AccountService.DisconnectInstitutionAsync), and
    // the reverse puts that snapshot back under its original ids — so a hand-entered bank (no Plaid
    // credentials to restore from) and a Plaid bank's Savings override, order, colour and history all come
    // back (Guard R-G11).
    private async void DisconnectInstitution(Institution institution)
    {
        // Only one undo window is ever in flight (the dog-ear holds one): a previous disconnect whose window
        // is still open has had its undo replaced, so it closes now and its Item is released at Plaid —
        // before this, starting a second disconnect stopped the first one's timer and never released it.
        await ClosePendingDisconnectAsync();

        var disconnected = await accounts.DisconnectInstitutionAsync(institution.Id, CancellationToken.None);
        if (disconnected is null)
            return;
        pendingDisconnect = disconnected;
        await ReloadAsync();

        // Plaid's /item/remove must not fire IMMEDIATELY: releasing the Item the instant the card
        // disappears would mean an owner who taps undo gets back an institution whose access token is
        // already dead at Plaid's end. It is deferred behind its own timer, cancelled by the same reverse
        // the dog-ear's undo calls, and skipped altogether for a hand-entered bank (blank token).
        dogEarState.BeginUndo(
            $"Disconnected {institution.Name}",
            reverse: () => _ = UndoDisconnectAsync(disconnected),
            DateTimeOffset.Now,
            TimeSpan.FromMilliseconds(Index.Domain.Design.DesignTokens.Motion.UndoWindowMs));
        dogEar.Refresh();

        pendingDisconnectReleaseTimer = Dispatcher.CreateTimer();
        pendingDisconnectReleaseTimer.Interval = TimeSpan.FromMilliseconds(Index.Domain.Design.DesignTokens.Motion.UndoWindowMs);
        pendingDisconnectReleaseTimer.IsRepeating = false;
        pendingDisconnectReleaseTimer.Tick += (_, _) => _ = ClosePendingDisconnectAsync();
        pendingDisconnectReleaseTimer.Start();
    }

    private async Task UndoDisconnectAsync(DisconnectedInstitution disconnected)
    {
        pendingDisconnectReleaseTimer?.Stop();
        pendingDisconnectReleaseTimer = null;
        if (ReferenceEquals(pendingDisconnect, disconnected))
            pendingDisconnect = null;

        await accounts.UndoDisconnectAsync(disconnected, CancellationToken.None);
        await ReloadAsync();
    }

    // The window closed (or was replaced) without an undo: release the Item at Plaid, once. A failure is
    // logged with its detail by AccountService and never undoes the local disconnect.
    private async Task ClosePendingDisconnectAsync()
    {
        pendingDisconnectReleaseTimer?.Stop();
        pendingDisconnectReleaseTimer = null;
        var disconnected = pendingDisconnect;
        pendingDisconnect = null;
        if (disconnected is null)
            return;

        await accounts.CommitDisconnectAsync(disconnected, plaid, logger, CancellationToken.None);
    }

    // ---- notices on the shared Waiting page (D161, Guard G25) ----

    private void RefreshNotices(IReadOnlyList<Institution> institutions)
    {
        var lastRefreshFailed = SettingsStore.LastRunOutcome.StartsWith("Failed", StringComparison.OrdinalIgnoreCase);
        var domainNotices = AccountNotices.Build(institutions, DateTimeOffset.Now, lastRefreshFailed);

        var newKeys = new HashSet<string>();
        foreach (var notice in domainNotices)
        {
            var key = $"{notice.Severity}:{notice.Headline}";
            newKeys.Add(key);
            dogEarState.SetNotice(key, ToDogEarNotice(notice));
        }

        // Withdraw every key this screen raised before that is not in the fresh set — a resolved
        // cause disappears because it was never asked for again, not because something dismissed it.
        foreach (var staleKey in lastNoticeKeys.Except(newKeys))
        {
            dogEarState.SetNotice(staleKey, null);
        }
        lastNoticeKeys = newKeys;

        dogEar.Refresh();
    }

    private DogEarNotice ToDogEarNotice(AccountNotice notice)
    {
        // The action carries the id it acts on (R6): the headlines name the institution, not the
        // account, so finding a target again by matching the headline's text can no longer work.
        Action? onAction = notice.Target switch
        {
            { Action: NoticeAction.Reconnect } target => () => _ = OpenReconnectFromNoticeAsync(target.Id),
            { Action: NoticeAction.OpenAccount } target => () => _ = OpenAccountFromNoticeAsync(target.Id),
            _ => null,
        };

        return new DogEarNotice(
            notice.Headline, notice.Detail, ToHue(notice.Severity), notice.ActionLabel, onAction, At: notice.At);
    }

    // D150: the Waiting page's Reconnect action opens PlaidLinkPage in UPDATE MODE against the failing
    // institution's existing access token — never a fresh Link (Guard G16). The institution is found by
    // the id the notice carries. Falls back to Settings if it has gone since (a disconnect between the
    // notice being raised and being tapped), rather than doing nothing.
    private async Task OpenReconnectFromNoticeAsync(Guid institutionId)
    {
        var institutions = await accounts.GetInstitutionsAsync(CancellationToken.None);
        var match = institutions.FirstOrDefault(i => i.Id == institutionId);

        if (match is null)
        {
            await Navigation.PushAsync(IPlatformApplication.Current!.Services.GetRequiredService<SetupPage>());
            return;
        }

        await PushPlaidLinkAsync(match);
    }

    // The one way onto the connect page from Accounts — the Add-an-account chooser and the Waiting
    // page's reconnect both come through here, so neither can reach it with no Plaid keys set. Settings
    // makes the same check before it pushes the page (SetupPage.OnConnectClicked); without it the owner
    // got a raw "Couldn't start Plaid Link" error from the link-token call instead of a plain sentence.
    // A reconnect (institution given) opens Link in UPDATE MODE against that institution's existing
    // access token — never a fresh Link (D150, Guard G16).
    private async Task PushPlaidLinkAsync(Institution? reconnecting = null)
    {
        if (!SettingsStore.IsConfigured)
        {
            // Said on Accounts, in the status line Settings also uses. A reconnect arrives from a page
            // stacked above Accounts, so turn back to it, and carry the sentence across the reload that
            // turning back triggers (ReloadAsync would otherwise replace it with the last refresh).
            connectSentence = ConnectCopy.NotConfigured;
            statusLabel.Text = connectSentence;
            if (Navigation.NavigationStack.Count > 1)
            {
                await Navigation.PopToRootAsync();
            }

            return;
        }

        var page = IPlatformApplication.Current!.Services.GetRequiredService<PlaidLinkPage>();
        if (reconnecting is not null)
        {
            page.BeginUpdateMode(reconnecting.Id, reconnecting.AccessToken, reconnecting.Name);
        }

        await Navigation.PushAsync(page);
    }

    // "Open Evergreen Home Lending" (a stale hand-entered balance) returns to Accounts and expands that account, by the
    // account id the notice carries (mockup 17: "turns back to Accounts with its post-it open").
    private async Task OpenAccountFromNoticeAsync(Guid accountId)
    {
        await Navigation.PopToRootAsync();
        var institutions = await accounts.GetInstitutionsAsync(CancellationToken.None);
        if (institutions.SelectMany(i => i.Accounts).Any(a => a.Id == accountId))
        {
            SetExpanded(accountId);
        }
        RenderInstitutions(institutions);
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

    // The fold turns the page to Waiting in this app's own frame (Accounts still lit), live over the
    // same state — the snapshot the event carries is not what the page reads.
    private void OnNoticesOpened(IReadOnlyList<DogEarNotice> opened) =>
        _ = Navigation.PushAsync(new LedgerWaitingPage(LedgerArea.Accounts, dogEarState));

    // ---- formatting (unchanged from the pre-Release-B screen) ----

    // Cents and the real minus sign: the Domain's AccountsMoney is the one formatter (Phase 4).
    internal static string Money(decimal value, string? isoCurrencyCode) => AccountsMoney.Cents(value, isoCurrencyCode);
}
