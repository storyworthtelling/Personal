using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Index.Android.Google;
using Index.Domain.External;
using Index.Domain.Health;
using Index.Domain.Household;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Infrastructure.Household;
using Index.Infrastructure.Services;
using Storyworthtelling.Maui.State;
using Storyworthtelling.Maui.Views;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;

namespace Index.Android.Pages;

public partial class SetupPage : LedgerBinderPage
{
    private readonly AccountService accounts;
    private readonly IPlaidClient plaid;
    private readonly VaultSync vaultSync;
    private readonly DriveVoiceOptions voiceOptions;
    private readonly BudgetService budgetService;
    // Release I: which install this is (D09/D25/D29/D35/D36/D90), settled once from the household
    // file and remembered (Phase 1.1) — this page reads the remembered answer, it does not re-derive it.
    private readonly InstallRoleService roles;
    // Brings the vault's banks back when a passphrase is saved (task 2.6): restore used to run only at
    // launch, so a passphrase typed afterwards restored nothing.
    private readonly PassphraseRestoreFlow restoreFlow;
    private readonly ILogger<SetupPage> logger;

    // A removed category's undo is the suite's dog-ear, a fixed corner of the page (task 2.7): the bar
    // that used to carry it sat in the Connected accounts section near the top, several screens above
    // the Categories list it answered to, and disappeared with that section.
    private readonly DogEarState dogEarState = new();
    private readonly DogEar dogEar;

    private List<InstitutionRow> rows = [];
    private InstitutionRow? pendingInstitutionRemoval;
    // The bar below serves the institution removal only; it sits in its own section, beside Remove.
    private IDispatcherTimer? undoTimer;

    // Set by the page that sends the owner here to type the passphrase (mockup 14, state 2), before the
    // page is pushed — the way PlaidLinkPage.BeginUpdateMode is — and consumed by the next appearance.
    private bool focusPassphraseOnAppearing;

    public SetupPage(
        AccountService accounts, IPlaidClient plaid, VaultSync vaultSync, DriveVoiceOptions voiceOptions,
        BudgetService budgetService, InstallRoleService roles, PassphraseRestoreFlow restoreFlow,
        ILogger<SetupPage> logger)
    {
        InitializeComponent();

        // The dog-ear is the page's own corner, not a child of the scrolling content: it must be where
        // the owner is looking wherever the list is scrolled to, so the frame holds it (SetCorner).
        dogEar = new DogEar(dogEarState);

        Wrap(LedgerArea.Settings, "Settings");
        SetCorner(dogEar);

        // Mockup 10: no pinned bar, every field saves itself. Each commits when it is left, and when entry
        // is completed from the keyboard; leaving the page (OnDisappearing) commits whatever is left.
        ClientIdEntry.Unfocused += OnFieldLeft;
        ClientIdEntry.Completed += OnFieldLeft;
        SecretEntry.Unfocused += OnFieldLeft;
        SecretEntry.Completed += OnFieldLeft;
        VaultPassphraseEntry.Unfocused += OnFieldLeft;
        VaultPassphraseEntry.Completed += OnFieldLeft;

        // The one shared formatter, so every app's foot line reads identically. "Ledger" was stale
        // from before the P11 rename — every namespace, the app id and the on-device label all say
        // Index now, and mockup 10 prints "Index 1.0.x" here.
        BuildLineLabel.Text = Storyworthtelling.Updates.BuildLine.Format(
            "Index", Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString,
            Microsoft.Maui.ApplicationModel.AppInfo.Current.PackageName);
        this.accounts = accounts;
        this.plaid = plaid;
        this.vaultSync = vaultSync;
        this.voiceOptions = voiceOptions;
        this.budgetService = budgetService;
        this.roles = roles;
        this.restoreFlow = restoreFlow;
        this.logger = logger;
    }

    /// <summary>Opens Settings with the vault passphrase field scrolled into view and focused, for the
    /// first-run screen whose only action is "Enter the passphrase". Call before pushing the page.</summary>
    public void BeginEnterPassphrase() => focusPassphraseOnAppearing = true;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // The remembered role, with no I/O: her screen is her screen from the first frame, rather than the
        // owner's until the role is read again. ApplyInstallRoleAsync below settles it if it was not.
        ApplyInstallRole(roles.Current);

        // Pre-fill the (usually hidden) credential entries so a save never wipes them; ApplyPlaidCredentialField
        // then decides whether to show them based on the credentials' last-known health.
        ClientIdEntry.Text = SettingsStore.PlaidClientId;
        SecretEntry.Text = SettingsStore.PlaidSecret;
        ApplyPlaidCredentialField();
        VaultPassphraseEntry.Text = SettingsStore.VaultPassphrase;

        if (focusPassphraseOnAppearing)
        {
            // Consumed once: coming back to Settings later must not grab the keyboard again.
            focusPassphraseOnAppearing = false;
            // A beat for the page to be laid out; scrolling before that has nothing to scroll within.
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), async () =>
            {
                try
                {
                    await PageScroll.ScrollToAsync(VaultPassphraseEntry, ScrollToPosition.MakeVisible, animated: true);
                    VaultPassphraseEntry.Focus();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not bring the passphrase field into view.");
                }
            });
        }

        var migrationError = SettingsStore.LastMigrationError;
        MigrationErrorCard.IsVisible = !string.IsNullOrEmpty(migrationError);
        MigrationErrorLabel.Text = migrationError;

        await ReloadConnectedAsync();

        TrackingOnlySwitch.IsToggled = SettingsStore.BudgetTrackingOnly;
        // Release E: the first appearance after this schema exists seeds BudgetCategory from whatever
        // SettingsStore.BudgetCategories' JSON list already held (the six starters, if nothing else
        // changed them yet) — a no-op on every later appearance, once any row exists.
        await budgetService.MigrateScaffoldIfNeededAsync(SettingsStore.BudgetCategories, CancellationToken.None);
        await ReloadCategoriesAsync();

        // Fire-and-forget, same pattern as MainPage's ShowWelcomeAsync: a Drive read shouldn't
        // block the rest of the page from appearing.
        _ = ApplySharedKeyStatusAsync();

        // Release I (D09/D36, mockup 11 frame 2): everything above runs unchanged on every install —
        // her local DB simply has nothing to load, the same quiet degrade any fresh install shows.
        // This is the one additional step that hides what her Settings must never show at all.
        await ApplyInstallRoleAsync();
    }

    // ---- Release I: which install this is, and her Settings' section visibility (D09/D36) ----

    private async Task ApplyInstallRoleAsync() =>
        ApplyInstallRole(await roles.ResolveAsync(CancellationToken.None));

    // Everything that differs between the two installs' Settings, in one place: which sections show and
    // the words in the ones both share (SettingsCopy, mockup 11). No I/O, so it can run on the remembered
    // role at the first frame and again on whatever the role settles as.
    private void ApplyInstallRole(InstallRole role)
    {
        var isHers = role == InstallRole.Hers;

        VaultHeadingLabel.Text = SettingsCopy.VaultHeading(role);
        var vaultIntro = SettingsCopy.VaultIntro(role);
        VaultIntroLabel.Text = vaultIntro;
        VaultIntroLabel.IsVisible = vaultIntro is not null;
        PassphraseNoteLabel.Text = SettingsCopy.PassphraseNote(role);

        var googleIntro = SettingsCopy.GoogleIntro(role);
        GoogleIntroLabel.Text = googleIntro;
        GoogleIntroLabel.IsVisible = googleIntro is not null;
        ApplyGoogleAccount(role);

        // D09/D36, mockup 11 frame 2: her Settings holds only Vault and Google account, plus the foot.
        // No Plaid, no shared-key status, no connect flow, no Budget group, no Diagnostics.
        PlaidSection.IsVisible = !isHers;
        PlaidDivider.IsVisible = !isHers;
        SharedKeysSection.IsVisible = !isHers;
        SharedKeysDivider.IsVisible = !isHers;
        ConnectedAccountsSection.IsVisible = !isHers;
        ConnectedAccountsDivider.IsVisible = !isHers;
        BudgetSection.IsVisible = !isHers;
        BudgetDivider.IsVisible = !isHers;
        DiagnosticsSection.IsVisible = !isHers;
    }

    // The Google account block. Hers (mockup 11): "Signed in" over "[address] · Drive, where the shared
    // balances and the voice are read", and the button only while signed out. The owner's: the button
    // and a "Signed in as …" line, as before.
    private void ApplyGoogleAccount(InstallRole role)
    {
        var email = SettingsStore.GoogleAccountEmail;
        var signedIn = !string.IsNullOrEmpty(email);
        var isHers = role == InstallRole.Hers;

        SignedInBlock.IsVisible = isHers && signedIn;
        SignedInHeadingLabel.Text = SettingsCopy.SignedInHeading;
        SignedInLineLabel.Text = signedIn ? SettingsCopy.SignedInLine(email) : string.Empty;
        VoiceSignInButton.IsVisible = SettingsCopy.ShowsSignIn(role, signedIn);
        VoiceStatusLabel.Text = isHers || !signedIn ? string.Empty : $"Signed in as {email}.";
    }

    // Reads whether the shared Claude key (Storyworthtelling.Voice, entered once in Folio) is present
    // in the Drive config Index already reads for the balances-screen greeting (DriveVoiceOptions is
    // wired the same way in MainPage). This is a REAL, live read — not a placeholder — but it reports
    // presence only, not a verified-working call: VoiceClient has no ICredentialHealthSink seam of its
    // own anywhere in the suite, so nothing ever marks this key Ok/Failing the way Plaid's is marked
    // below. "Working" here means "the key is there to read", which is what makes a silently-degraded
    // greeting diagnosable at all (D80) — it does not mean "Claude just accepted a real call".
    private async Task ApplySharedKeyStatusAsync()
    {
        var hasKey = false;
        try
        {
            var options = await voiceOptions.RefreshAsync(CancellationToken.None);
            hasKey = !string.IsNullOrEmpty(options.ApiKey);
        }
        catch
        {
            // Best-effort: Drive being unreachable just means "can't tell right now".
        }

        SharedKeyStatusLabel.Text = hasKey ? "✓ Working" : "Not set.";
    }

    // Shows the Plaid client id/secret fields only when there's something to do: no credentials stored yet,
    // or Plaid just rejected them. Working credentials collapse to a "Working ✓" line with a Replace button,
    // so the screen stays quiet and the secret isn't left on display (the vault keeps the backup).
    private void ApplyPlaidCredentialField()
    {
        var isSet = !string.IsNullOrEmpty(SettingsStore.PlaidClientId) &&
                    !string.IsNullOrEmpty(SettingsStore.PlaidSecret);
        var state = CredentialFieldState.Decide(isSet, CredentialHealth().Get(CredentialIds.Plaid));
        PlaidCredentialFields.IsVisible = state.ShowEntry;
        PlaidStatusRow.IsVisible = !state.ShowEntry || state.IsWarning;
        PlaidReplaceButton.IsVisible = !state.ShowEntry;
        PlaidStatusLabel.Text = state.StatusText;
        PlaidStatusLabel.Style = LookupStyle(state.IsWarning ? "ErrorLabel" : "SecondaryLabel");
    }

    private static CredentialHealthReport CredentialHealth() =>
        IPlatformApplication.Current!.Services.GetRequiredService<CredentialHealthStore>().Load();

    // Styles live in the app-wide merged dictionaries, which a page's own Resources indexer doesn't search;
    // TryGetValue on the application resources walks the merged dictionaries.
    private static Style? LookupStyle(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true ? value as Style : null;

    // Deliberate rotation of working credentials: hide the status row and open the fields (kept empty so the
    // new values are typed fresh rather than edited over the old ones).
    private void OnReplacePlaidClicked(object? sender, EventArgs e)
    {
        PlaidStatusRow.IsVisible = false;
        ClientIdEntry.Text = string.Empty;
        SecretEntry.Text = string.Empty;
        PlaidCredentialFields.IsVisible = true;
        ClientIdEntry.Focus();
    }

    // Opens the system Google account picker and requests read-only Drive consent, so Ledger can read the
    // shared owner's-voice profile. The result returns through MainActivity.OnActivityResult.
    private async void OnVoiceSignInClicked(object sender, EventArgs e)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
            return;
        try
        {
            await DeviceAccountSignIn.SignIn(activity);
            // A different account voids the remembered role; settle it again and redraw for whoever this is.
            await ApplyInstallRoleAsync();
        }
        catch (Exception ex)
        {
            VoiceStatusLabel.Text = $"Sign-in didn't complete. {ex.Message}";
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Don't leave a removal half-done if the user navigates away mid-undo.
        CommitPendingUndo();
        // The safety net for a field still being typed in when the page goes (the binder tab and the back
        // button commit before they leave, below, so this is normally a no-op: an unchanged value does
        // nothing).
        _ = CommitSafelyAsync();
    }

    /// <summary>A tab turn or back press leaves only after the fields have committed — as Save used to
    /// make the page wait for the restore to START before it went, so Accounts never opens on "Nothing
    /// connected yet" with an Add button while the banks are on their way back.</summary>
    protected override bool CommitsBeforeLeaving => true;

    protected override Task BeforeLeavingAsync() => CommitSafelyAsync();

    private async Task ReloadConnectedAsync()
    {
        var institutions = await accounts.GetInstitutionsAsync(CancellationToken.None);
        rows = institutions.Select(InstitutionRow.From).ToList();
        ConnectedList.BindingContext = rows;
        NoAccountsLabel.IsVisible = rows.Count == 0;
    }

    private async void OnFieldLeft(object? sender, EventArgs e) => await CommitSafelyAsync();

    // Every caller of a commit is a fire-and-forget event handler or a navigation hook, where a throw
    // would take the process down or strand the navigation. The failure is logged with its stack trace
    // and the next commit starts clean; nothing is lost, since the fields still hold what was typed.
    private async Task CommitSafelyAsync()
    {
        try
        {
            await CommitSettingsAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Committing a Settings field failed.");
        }
    }

    // Commits whatever the three fields hold that differs from what is stored (task 2.7; mockup 10: no
    // pinned bar, every field saves itself). The decision is the Domain's: an unchanged value does nothing
    // — no write, no restore, no vault publish — and the Plaid pair never commits half-typed, so leaving
    // an emptied box cannot blank credentials that were still working.
    //
    // What follows the store is task 2.6's flow, unchanged: settle the role, restore ONCE and only on a
    // changed passphrase, and publish only after the restore has finished and only when this install may
    // touch Plaid. It is also called for a Plaid-only change, where it restores nothing but tells the
    // publish about a restore still in flight, so the vault's banks are not overwritten by an empty list.
    private async Task CommitSettingsAsync()
    {
        // The value being replaced, read before it is: "changed" cannot be told afterwards.
        var passphraseBefore = SettingsStore.VaultPassphrase;
        var plan = SettingsCommitPlan.Decide(
            SettingsStore.PlaidClientId, SettingsStore.PlaidSecret, passphraseBefore,
            ClientIdEntry.Text, SecretEntry.Text, VaultPassphraseEntry.Text);
        if (!plan.Any)
        {
            return;
        }

        if (plan.StorePlaid)
        {
            SettingsStore.PlaidClientId = ClientIdEntry.Text?.Trim() ?? string.Empty;
            SettingsStore.PlaidSecret = SecretEntry.Text?.Trim() ?? string.Empty;
            // New credentials: the status row reflects them (a working pair collapses to "Working ✓").
            ApplyPlaidCredentialField();
        }

        if (plan.StorePassphrase)
        {
            SettingsStore.VaultPassphrase = VaultPassphraseEntry.Text ?? string.Empty;
        }

        // A new passphrase or account voids the remembered role (SettingsStore), so it is settled again
        // before anything is written or restored: her install must neither publish a vault section nor
        // leave the owner's workers scheduled. On an install that may touch Plaid a CHANGED passphrase
        // also brings the vault's banks back and schedules the refresh the launch skipped, without a
        // restart; the restore runs in the background and Accounts shows it as it fills in.
        var run = await restoreFlow.BeginAsync(passphraseBefore, SettingsStore.VaultPassphrase, CancellationToken.None);
        var role = roles.Current;
        // The role may just have settled as hers: this page changes with it, in place, and stays.
        ApplyInstallRole(role);
        // Back up the Plaid credentials + connected banks to the encrypted Drive vault. The backup is
        // optional — the settings already work locally — but a failure now surfaces instead of vanishing: a
        // silently-skipped backup is exactly what left the vault empty after the cutover.
        if (InstallRoleGate.MayRunPlaidWork(role))
        {
            await PublishToVaultAsync(run);
        }
        // Success is a state change, not a toast (DESIGN.md §7), and the page stays where it is.
    }

    // Publishes and, only when a passphrase was set but the backup didn't land, tells the owner. A
    // no-passphrase skip stays quiet (the app just isn't in the vault yet).
    private async Task PublishToVaultAsync(RestoreRun run)
    {
        if (!run.Completion.IsCompleted)
        {
            // A restore is still bringing the banks back. Publishing now would write this install's (still
            // empty) bank list over the vault's, erasing the only copy that survives a factory reset, so
            // the backup waits for the restore. Settings will be gone by then, so a failure goes to the
            // diagnostics log rather than an alert on a page nobody is looking at.
            _ = PublishAfterRestoreAsync(run.Completion);
            return;
        }

        SecretsVaultPublishResult result;
        try { result = await vaultSync.PublishAsync(CancellationToken.None); }
        catch (Exception ex) { result = SecretsVaultPublishResult.WriteFailed(ex.Message); }
        if (result.NeedsAttention)
            await DisplayAlert("Vault backup didn't complete", result.Message, "OK");
    }

    private async Task PublishAfterRestoreAsync(Task restore)
    {
        try
        {
            await restore;
            var result = await vaultSync.PublishAsync(CancellationToken.None);
            if (result.NeedsAttention)
            {
                logger.LogWarning("Vault backup after the restore did not complete: {Message}", result.Message);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Vault backup after the restore did not complete.");
        }
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        // Tapping a button does not always take focus from the box being typed in, so what is typed there
        // is committed first rather than waiting for a blur that may not come.
        await CommitSafelyAsync();
        if (!SettingsStore.IsConfigured)
        {
            ConnectStatusLabel.Text = "Enter your Plaid client ID and secret before connecting.";
            return;
        }

        await Navigation.PushAsync(IPlatformApplication.Current!.Services.GetRequiredService<PlaidLinkPage>());
    }

    private void OnRemoveClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: InstitutionRow row })
            return;

        // A previous pending institution removal commits now — only one is ever in flight at a time.
        CommitPendingUndo();

        rows.Remove(row);
        ConnectedList.BindingContext = rows.ToList();
        NoAccountsLabel.IsVisible = rows.Count == 0;

        pendingInstitutionRemoval = row;
        BeginUndo($"Removed {row.Name}.");
    }

    private void OnUndoClicked(object? sender, EventArgs e)
    {
        undoTimer?.Stop();
        undoTimer = null;
        UndoBar.IsVisible = false;

        if (pendingInstitutionRemoval is not null)
        {
            pendingInstitutionRemoval = null;
            _ = ReloadConnectedAsync();
        }
    }

    // Bound to the dog-ear's turn (DogEarState.BeginUndo's reverse): puts the soft-deleted row back by Id.
    private async Task UndoRemoveCategoryAsync(BudgetCategory category)
    {
        await budgetService.SetCategoryActiveAsync(category.Id, isActive: true, CancellationToken.None);
        await ReloadCategoriesAsync();
    }

    private void BeginUndo(string message)
    {
        UndoLabel.Text = message;
        UndoBar.IsVisible = true;

        undoTimer = Dispatcher.CreateTimer();
        undoTimer.Interval = TimeSpan.FromMilliseconds(Index.Domain.Design.DesignTokens.Motion.UndoWindowMs);
        undoTimer.IsRepeating = false;
        undoTimer.Tick += (_, _) => CommitPendingUndo();
        undoTimer.Start();
    }

    private void CommitPendingUndo()
    {
        undoTimer?.Stop();
        undoTimer = null;
        UndoBar.IsVisible = false;

        var row = pendingInstitutionRemoval;
        pendingInstitutionRemoval = null;
        if (row is null)
            return;

        // Best-effort release at Plaid (stops any server-side subscription), then delete locally.
        _ = Task.Run(async () =>
        {
            // Skipped for a hand-entered bank (blank token); a failure is logged, never thrown — the local
            // record goes either way.
            await PlaidItemRelease.ReleaseAsync(plaid, row.AccessToken, row.Name, logger, CancellationToken.None);
            await accounts.RemoveInstitutionAsync(row.Id, CancellationToken.None);
            // Keep the vault's backup in step with the removal.
            try { await vaultSync.PublishAsync(CancellationToken.None); }
            catch { /* Best-effort backup. */ }
        });
    }

    // ---- Budget (D96, D55, D110) ----

    private void OnTrackingOnlyToggled(object? sender, ToggledEventArgs e) =>
        SettingsStore.BudgetTrackingOnly = e.Value;

    // Release E: BudgetService/BudgetCategory now owns the list (D55's soft delete); this page keeps
    // the last-loaded rows so a tap can look a display name back up to its Id (CategoriesList still
    // binds a plain List<string> — the XAML's own binding shape is unchanged, per this task's scope).
    private List<BudgetCategory> categoryRows = [];

    private async Task ReloadCategoriesAsync()
    {
        categoryRows = (await budgetService.GetActiveCategoriesAsync(CancellationToken.None)).ToList();
        CategoriesList.BindingContext = categoryRows.Select(c => c.Name).ToList();
    }

    private async void OnAddCategoryClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("Add a category", "Name");
        if (string.IsNullOrWhiteSpace(name))
            return;

        var trimmed = name.Trim();
        if (categoryRows.Any(c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            return;

        await budgetService.AddCategoryAsync(trimmed, CancellationToken.None);
        await ReloadCategoriesAsync();
    }

    // Reached by a tap rather than mockup 10's long-press — see the note on CategoriesList in the
    // XAML. Rename/Move/Remove all really work; only the gesture that opens them is a substitution.
    private async void OnCategoryTapped(object? sender, EventArgs e)
    {
        if (sender is not Grid { BindingContext: string categoryName })
            return;

        var index = categoryRows.FindIndex(c => c.Name == categoryName);
        if (index < 0)
            return;
        var category = categoryRows[index];

        var options = new List<string> { "Rename" };
        if (index > 0) options.Add("Move up");
        if (index < categoryRows.Count - 1) options.Add("Move down");
        options.Add("Remove");

        var action = await DisplayActionSheet(categoryName, "Cancel", null, options.ToArray());
        switch (action)
        {
            case "Rename":
                var newName = await DisplayPromptAsync("Rename category", "Name", initialValue: categoryName);
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    var trimmed = newName.Trim();
                    if (!string.Equals(trimmed, categoryName, StringComparison.Ordinal) &&
                        !categoryRows.Any(c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                    {
                        await budgetService.RenameCategoryAsync(category.Id, trimmed, CancellationToken.None);
                        await ReloadCategoriesAsync();
                    }
                }
                break;

            case "Move up":
                await budgetService.MoveCategoryUpAsync(category.Id, CancellationToken.None);
                await ReloadCategoriesAsync();
                break;

            case "Move down":
                await budgetService.MoveCategoryDownAsync(category.Id, CancellationToken.None);
                await ReloadCategoriesAsync();
                break;

            case "Remove":
                // Undo-first (DESIGN.md §7): the soft delete (D55) has already written through, so its past
                // months keep their totals, and the dog-ear — fixed in the corner, wherever the list is
                // scrolled to — holds the way back for the suite's one undo window.
                await budgetService.SetCategoryActiveAsync(category.Id, isActive: false, CancellationToken.None);
                await ReloadCategoriesAsync();
                dogEarState.BeginUndo(
                    $"Removed {categoryName}",
                    reverse: () => _ = UndoRemoveCategoryAsync(category),
                    DateTimeOffset.Now,
                    TimeSpan.FromMilliseconds(Index.Domain.Design.DesignTokens.Motion.UndoWindowMs));
                dogEar.Refresh();
                break;
        }
    }

    private static string RefreshLogPath => Path.Combine(FileSystem.AppDataDirectory, "refresh.log");
    private static string CrashLogPath => Path.Combine(FileSystem.AppDataDirectory, "crash.log");

    // D145's summary section: version + package, each bank's last refresh and error, and credential
    // health. HARD REQUIREMENT (guarded by DiagnosticsRedactionTests): never write institution.AccessToken,
    // a Plaid client secret, or an account's Mask/AccountId — only the institution's display Name and
    // the human-readable status the rest of the app already shows unprotected (MainPage's own account
    // list already surfaces LastError the same way).
    private async Task<string> BuildDiagnosticsSummaryAsync()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Storyworthtelling.Updates.BuildLine.Format(
            "Index", Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString,
            Microsoft.Maui.ApplicationModel.AppInfo.Current.PackageName));
        sb.AppendLine();

        sb.AppendLine("Credential health:");
        var health = CredentialHealth();
        var plaidState = CredentialFieldState.Decide(SettingsStore.IsConfigured, health.Get(CredentialIds.Plaid));
        sb.AppendLine($"  Plaid: {plaidState.StatusText}");

        var claudeSet = false;
        try
        {
            var options = await voiceOptions.RefreshAsync(CancellationToken.None);
            claudeSet = !string.IsNullOrEmpty(options.ApiKey);
        }
        catch { /* best-effort: Drive being unreachable just means "can't tell right now" */ }
        sb.AppendLine($"  Claude (voice, set in Folio): {(claudeSet ? "Working" : "Not set.")}");
        sb.AppendLine();

        sb.AppendLine("Connected banks:");
        var institutions = await accounts.GetInstitutionsAsync(CancellationToken.None);
        if (institutions.Count == 0)
        {
            sb.AppendLine("  (none connected)");
        }
        else
        {
            foreach (var institution in institutions)
            {
                var when = institution.LastRefreshedUtc is { } refreshed
                    ? "last refresh " + RelativeTime.Format(refreshed.LocalDateTime, DateTime.Now)
                    : "never refreshed";
                var error = string.IsNullOrEmpty(institution.LastError) ? "no error" : institution.LastError;
                sb.AppendLine($"  {institution.Name}: {when}; {error}");
            }
        }
        sb.AppendLine();

        sb.AppendLine($"Last background run: {SettingsStore.LastRunSummary}");
        if (!string.IsNullOrEmpty(SettingsStore.LastMigrationError))
            sb.AppendLine($"Last migration error: {SettingsStore.LastMigrationError}");

        return sb.ToString();
    }

    // One shareable file: the summary above, plus the running refresh log (every warning/error logged
    // across Index via PipelineFileLogger — connecting, refreshing, the vault, Post's balance handoff,
    // and everything else once those parts exist) and the crash log. Replaces "Share refresh log"
    // (D37/D145) — that only ever bundled the two logs; this adds the summary in front of them.
    private async void OnExportDiagnosticsClicked(object? sender, EventArgs e)
    {
        try
        {
            var summary = await BuildDiagnosticsSummaryAsync();
            var bundle = new System.Text.StringBuilder()
                .AppendLine("===== DIAGNOSTICS SUMMARY =====").AppendLine().AppendLine(summary).AppendLine()
                .AppendLine("===== REFRESH LOG =====").AppendLine().AppendLine(ReadOrEmpty(RefreshLogPath)).AppendLine()
                .AppendLine("===== CRASH LOG =====").AppendLine().AppendLine(ReadOrEmpty(CrashLogPath))
                .ToString();
            var path = Path.Combine(FileSystem.CacheDirectory, $"index-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.txt");
            await File.WriteAllTextAsync(path, bundle);
            await Share.RequestAsync(new ShareFileRequest { Title = "Index diagnostics", File = new ShareFile(path) });
        }
        catch (Exception ex)
        {
            DiagnosticsStatusLabel.Text = "Couldn't export diagnostics: " + ex.Message;
        }
    }

    private static string ReadOrEmpty(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : "(none)";

    public sealed class InstitutionRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string AccessToken { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;

        public static InstitutionRow From(Institution institution)
        {
            var count = institution.Accounts.Count;
            var when = institution.LastRefreshedUtc is { } refreshed
                ? "updated " + RelativeTime.Format(refreshed.LocalDateTime, DateTime.Now)
                : "not refreshed yet";
            var accountsText = count == 1 ? "1 account" : $"{count} accounts";
            return new InstitutionRow
            {
                Id = institution.Id,
                Name = institution.Name,
                AccessToken = institution.AccessToken,
                Summary = $"{accountsText} · {when}"
            };
        }
    }
}
