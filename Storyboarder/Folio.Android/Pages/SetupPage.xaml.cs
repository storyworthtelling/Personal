using Storyworthtelling.Maui;
using Storyworthtelling.Folds;
using Storyworthtelling.Google.Android;
using Folio.Infrastructure.Vault;
using Folio.Domain.Diagnostics;
using Storyworthtelling.Maui.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Folio.Android.Google;
using Folio.Android.Platforms.Android;
using Folio.Domain.Health;
using Folio.Domain.Identity;
using Folio.Infrastructure.Services;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;
using Storyworthtelling.Maui.Views;

namespace Folio.Android.Pages;

public partial class SetupPage : FolioBinderPage
{
    private static string PipelineLogPath => System.IO.Path.Combine(FileSystem.AppDataDirectory, "pipeline.log");
    private static string CrashLogPath => System.IO.Path.Combine(FileSystem.AppDataDirectory, "crash.log");

    public SetupPage()
    {
        InitializeComponent();
        Wrap(FolioArea.Settings, "Settings");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // A sign-in Google turned away says so on the button (audit 11 §4).
        GoogleSignInButton.Text = Storyworthtelling.Maui.Health.GoogleSignInHealth.ButtonLabel("Sign in with Google");
        // Pre-fill the (usually hidden) entries so a save never wipes a stored key; ApplyCredentialFields
        // then decides which fields to actually show based on each key's last-known health.
        ClaudeKeyEntry.Text = SettingsStore.ClaudeApiKey;
        GeminiKeyEntry.Text = SettingsStore.GeminiApiKey;
        ApplyCredentialFields();
        // Where each key is made: the provider's console, from the suite's one table.
        KeyConsoleLink.Fill(ClaudeConsoleLink, CredentialIds.Claude);
        KeyConsoleLink.Fill(GeminiConsoleLink, CredentialIds.Gemini);
        KeyConsoleLink.Fill(GoogleCloudConsoleLink, KeyConsoles.GoogleOAuthClient);
        KeyConsoleLink.Fill(RemarkableConsoleLink, Storyworthtelling.Remarkable.RemarkableCredentialId.Value);
        VaultPassphraseEntry.Text = SettingsStore.VaultPassphrase;
        ArchiveSwitch.IsToggled = SettingsStore.ArchiveToDrive;
        FreeSpaceSwitch.IsToggled = SettingsStore.FreeLocalCopiesAfterArchive;
        // Reclaiming space only means anything once the copies are safely elsewhere.
        FreeSpaceSwitch.IsEnabled = ArchiveSwitch.IsToggled;
        RemarkableSwitch.IsToggled = SettingsStore.DeliverToRemarkable;
        UpdateRemarkableStatus();
        UpdateGoogleStatus();
        SigningInfoLabel.Text = SigningInfoText();
        BuildInfoLabel.Text = BuildInfoText();

        var migrationError = SettingsStore.LastMigrationError;
        MigrationErrorCard.IsVisible = !string.IsNullOrEmpty(migrationError);
        if (MigrationErrorCard.IsVisible)
            MigrationErrorLabel.Text =
                "The last app update couldn't finish updating your library, so work is paused. " +
                "Restoring a recent backup usually fixes it. Details: " + migrationError;
    }

    // Shows each API-key field only when there's something to do: no key stored yet, or a live call with
    // the stored key was just rejected. A working key is not on the screen at all (owner's ruling, audit
    // W8.4b): the vault keeps the backup.
    private void ApplyCredentialFields()
    {
        var report = CredentialHealth();
        var claude = ApplyCredentialField(
            CredentialIds.Claude, report, SettingsStore.ClaudeApiKey, ClaudeKeyBlock, ClaudeStatusLabel);
        var gemini = ApplyCredentialField(
            CredentialIds.Gemini, report, SettingsStore.GeminiApiKey, GeminiKeyBlock, GeminiStatusLabel);
        ApiKeysSection.IsVisible = claude || gemini;
    }

    private static CredentialHealthReport CredentialHealth() =>
        IPlatformApplication.Current!.Services.GetRequiredService<Storyworthtelling.Voice.Health.CredentialHealthStore>().Load();

    // Returns whether the key is on the screen. A refusal says so above the field; a key never entered
    // needs no line, since the empty field with its placeholder says it.
    private static bool ApplyCredentialField(
        string credentialId, CredentialHealthReport report, string storedValue, View block, Label statusLabel)
    {
        var state = CredentialFieldState.Decide(!string.IsNullOrEmpty(storedValue), report.Get(credentialId));
        block.IsVisible = state.Appears;
        statusLabel.IsVisible = state.IsWarning;
        statusLabel.Text = state.StatusText;
        return state.Appears;
    }

    private static ProjectBackupService BackupService() =>
        IPlatformApplication.Current!.Services.GetRequiredService<ProjectBackupService>();

    // Release R: the owner sees which books and roughly what it costs before anything is spent. The card is
    // the only way in; the healer never starts a regeneration.
    private async void OnRegenerateClicked(object sender, EventArgs e)
    {
        try
        {
            using var scope = IPlatformApplication.Current!.Services.CreateScope();
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            var card = Folio.Domain.Pipeline.RegenerationCard.For(await projects.RegenerationCandidatesAsync(CancellationToken.None));
            if (card.Action is null)
            {
                RegenerateStatusLabel.Text = card.Title + ".";
                return;
            }

            var answer = await PaperCards.Choose(this, card.Title, card.Subtitle, [card.Action], lead: card.Action);
            if (answer != card.Action)
                return;

            var started = await projects.StartRegenerationAsync(CancellationToken.None);
            PipelineWorker.RunNow(global::Android.App.Application.Context);
            RegenerateStatusLabel.Text = started == 1
                ? "One book is being regenerated. Its report in Drive is replaced when it is done."
                : $"{started} books are being regenerated, a series in story order. Each report in Drive is replaced when it is done.";
        }
        catch (Exception ex)
        {
            RegenerateStatusLabel.Text = Storyworthtelling.Diagnostics.PlainError.Describe(ex, "Folio");
        }
    }

    private async void OnBackupClicked(object sender, EventArgs e)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            BackupStatusLabel.Text = "Couldn't start the backup (no active window).";
            return;
        }

        try
        {
            var suggestedName = $"canonizer-backup-{DateTime.Now:yyyyMMdd-HHmm}.zip";
            var uri = await SafFilePicker.CreateDocumentAsync(activity, "application/zip", suggestedName);
            if (uri is null)
            {
                BackupStatusLabel.Text = "Backup cancelled.";
                return;
            }

            BackupStatusLabel.Text = "Backing up your progress…";
            var service = BackupService();
            await Task.Run(() =>
            {
                using var stream = activity.ContentResolver!.OpenOutputStream(uri)
                    ?? throw new IOException("Could not open the chosen file for writing.");
                service.ExportAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
            });
            BackupStatusLabel.Text = "Progress backed up.";
        }
        catch (Exception ex)
        {
            BackupStatusLabel.Text = "Backup failed: " + Storyworthtelling.Diagnostics.PlainError.Describe(ex);
        }
    }

    private async void OnRestoreClicked(object sender, EventArgs e)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            BackupStatusLabel.Text = "Couldn't start the restore (no active window).";
            return;
        }

        // No confirmation. Choosing a particular file out of a picker is already a deliberate act,
        // and a prompt in front of it asks the same question twice — what it replaces is said on the
        // page instead, where it can be read before starting rather than while being interrupted.
        try
        {
            // "*/*" so the backup is always selectable — some providers report the zip under a
            // different MIME; ImportAsync validates the contents and rejects a wrong file cleanly.
            var uri = await SafFilePicker.OpenDocumentAsync(activity, "*/*");
            if (uri is null)
            {
                BackupStatusLabel.Text = "Restore cancelled.";
                return;
            }

            BackupStatusLabel.Text = "Restoring your progress…";
            // Stop the worker so it isn't reading files we're about to swap out.
            PipelineWorker.CancelAll(activity);
            var service = BackupService();
            await Task.Run(() =>
            {
                using var stream = activity.ContentResolver!.OpenInputStream(uri)
                    ?? throw new IOException("Could not open the chosen file for reading.");
                service.ImportAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
            });

            // The restored database may predate a schema change; bring it up to date, then resume.
            MainApplication.RunMigrations();
            PipelineWorker.RunNow(activity);
            BackupStatusLabel.Text = "Restored. Your conversion will resume shortly.";
        }
        catch (Exception ex)
        {
            // A failed restore leaves the project as it was (ProjectBackupService swaps only once the new
            // one is complete), so the message says so.
            BackupStatusLabel.Text = "Restore failed, and your project is as it was: " + Storyworthtelling.Diagnostics.PlainError.Describe(ex);
        }
        finally
        {
            // The worker was stopped for the restore; it must come back whether or not the restore worked
            // (error-handling §16), or a failed restore would also stop every book.
            PipelineWorker.Schedule(activity);
        }
    }

    private static bool HasContent(string path) =>
        System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 0;

    private static string ReadOrEmpty(string path) =>
        HasContent(path) ? System.IO.File.ReadAllText(path) : "(empty)";

    // A plain-text snapshot of the current pipeline state and anything troubling, built from the DB by
    // ProjectService plus the settings that live in this head's Preferences.
    private static async Task<string> BuildDiagnosticsReport()
    {
        using var scope = IPlatformApplication.Current!.Services.CreateScope();
        var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
        var context = new DiagnosticsContext(
            ClaudeKeySet: !string.IsNullOrEmpty(SettingsStore.ClaudeApiKey),
            GeminiKeySet: !string.IsNullOrEmpty(SettingsStore.GeminiApiKey),
            GoogleAccount: SettingsStore.GoogleAccountEmail,
            LastRunOutcome: SettingsStore.LastRunOutcome,
            LastRunAtUtc: SettingsStore.LastRunAtUtc == DateTime.MinValue
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(SettingsStore.LastRunAtUtc, DateTimeKind.Utc)),
            MigrationError: string.IsNullOrEmpty(SettingsStore.LastMigrationError) ? null : SettingsStore.LastMigrationError);
        return await projects.BuildDiagnosticsReportAsync(context, CancellationToken.None);
    }

    private async void OnStatusSnapshotClicked(object sender, EventArgs e)
    {
        string report;
        try
        {
            report = await BuildDiagnosticsReport();
        }
        catch (Exception ex)
        {
            Say(DiagnosticsStatusLabel, "The snapshot could not be built: " + Storyworthtelling.Diagnostics.PlainError.Describe(ex), problem: true);
            return;
        }

        // Read on a card, with what to do about it (audit W8.2b: an action sheet, then an alert).
        var preview = report.Length > 3000 ? report[..3000] + "\n\n(cut short here — Share sends the full snapshot)" : report;
        var action = await PaperCards.ShowText(this, "Status snapshot", preview, "Copy", "Share");
        if (action == "Copy")
        {
            await Clipboard.SetTextAsync(report);
            Say(DiagnosticsStatusLabel, "Status snapshot copied.", problem: false);
        }
        else if (action == "Share")
        {
            await ShareText("folio-status.txt", report, "Folio status snapshot");
        }
    }

    // Bundles the status snapshot + both logs into one shareable file — so the crash log comes along
    // even when there's no crash to show on its own.
    private async void OnExportDiagnosticsClicked(object sender, EventArgs e)
    {
        try
        {
            var report = await BuildDiagnosticsReport();
            var bundle = new System.Text.StringBuilder()
                .AppendLine("===== STATUS SNAPSHOT =====").AppendLine().AppendLine(report).AppendLine()
                .AppendLine("===== PIPELINE LOG =====").AppendLine().AppendLine(MauiLog.Current?.ReadAll() ?? ReadOrEmpty(PipelineLogPath)).AppendLine()
                .AppendLine("===== CRASH LOG =====").AppendLine().AppendLine(ReadOrEmpty(CrashLogPath))
                .ToString();
            await ShareText($"folio-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.txt", bundle, "Folio diagnostics");
        }
        catch (Exception ex)
        {
            Say(DiagnosticsStatusLabel, "The diagnostics could not be exported: " + Storyworthtelling.Diagnostics.PlainError.Describe(ex), problem: true);
        }
    }

    // Writes text to a cache file and opens the share sheet (Share needs a file, not a raw string).
    private static async Task ShareText(string fileName, string content, string title)
    {
        var path = System.IO.Path.Combine(FileSystem.CacheDirectory, fileName);
        await System.IO.File.WriteAllTextAsync(path, content);
        await Share.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path) });
    }

    private async void OnPipelineLogClicked(object sender, EventArgs e) =>
        await ShowLog("Pipeline log", PipelineLogPath, () => MauiLog.Current?.Clear());

    private async void OnCrashLogClicked(object sender, EventArgs e) =>
        await ShowLog("Crash log", CrashLogPath, () => System.IO.File.Delete(CrashLogPath));

    // Shared View & copy / Share / Clear flow for a diagnostic log file (mirrors Foreword). The
    // buttons stay visible even when a log is empty, so the crash log is always reachable.
    private async Task ShowLog(string title, string path, Action clear)
    {
        if (!HasContent(path))
        {
            Say(DiagnosticsStatusLabel, $"No {title.ToLowerInvariant()} recorded yet.", problem: false);
            return;
        }

        var log = System.IO.File.ReadAllText(path);
        var preview = log.Length > 3000 ? "(the last part — Share sends the whole log)\n\n" + log[^3000..] : log;
        var action = await PaperCards.ShowText(this, title, preview, "Clear", "Copy", "Share");
        if (action == "Copy")
        {
            await Clipboard.SetTextAsync(log);
            Say(DiagnosticsStatusLabel, $"{title} copied.", problem: false);
        }
        else if (action == "Share")
        {
            await Share.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path) });
        }
        else if (action == "Clear")
        {
            // Undo-first (§7): the words are held for the fold's window and written back on undo.
            clear();
            Say(DiagnosticsStatusLabel, $"{title} cleared.", problem: false);
            OfferUndo($"Cleared the {title.ToLowerInvariant()}.", () =>
            {
                System.IO.File.WriteAllText(path, log);
                Say(DiagnosticsStatusLabel, $"{title} put back.", problem: false);
            });
        }
    }

    private const string VaultBackupNotice = "vault-backup";

    // One line, in the Problem hue when it is a problem and its own quiet colour when it is not.
    private static void Say(Label label, string text, bool problem)
    {
        label.Text = text;
        label.TextColor = problem ? PaperResources.Colour("ProblemText") : PaperResources.Colour("ProgressText");
    }

    private void UpdateGoogleStatus() =>
        GoogleStatusLabel.Text = SettingsStore.IsGoogleConfigured
            ? $"Signed in as {SettingsStore.GoogleAccountEmail} ✓"
            : "Not signed in";

    private static string SigningInfoText() =>
        $"package: {AppSigningInfo.PackageName}\nSHA-1: {AppSigningInfo.Sha1Fingerprint()}";

    // The installed build, readable on the locked-down phone (no access to system app info). The build
    // string is the versionCode the GitHub update channel compares, so this is what confirms an update landed.
    // The suite's one build line (BuildLine.Format): name, version, package.
    private static string BuildInfoText()
    {
        try
        {
            return Storyworthtelling.Updates.BuildLine.Format(
                FolioIdentity.DisplayName, AppInfo.Current.VersionString, AppInfo.Current.PackageName);
        }
        catch (Exception)
        {
            return Storyworthtelling.Updates.BuildLine.Format(FolioIdentity.DisplayName, null, AppSigningInfo.PackageName);
        }
    }

    private async void OnGoogleSignInClicked(object sender, EventArgs e)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            Say(GoogleStatusLabel, "Sign-in could not start: Folio has no open window. Try again.", problem: true);
            return;
        }

        try
        {
            var email = await DeviceAccountSignIn.SignIn(activity);
            UpdateGoogleStatus();
            // The status line below the button is the state change.
            Say(GoogleStatusLabel, $"Signed in as {email}.", problem: false);
        }
        catch (Exception ex) when (ex.ToString().Contains("UnregisteredOnApiConsole", StringComparison.OrdinalIgnoreCase))
        {
            var info = SigningInfoText();
            await Clipboard.SetTextAsync(info);
            // Said under the button, beside the one-time setup card that already explains the console.
            Say(GoogleStatusLabel,
                "Google turned the sign-in away: this app is not registered with it yet. The package and SHA-1 "
                + "are copied; set them up as the card above says, then sign in again.", problem: true);
        }
        catch (Exception ex)
        {
            await Clipboard.SetTextAsync(ex.ToString());
            Say(GoogleStatusLabel, "Sign-in did not finish: " + Storyworthtelling.Diagnostics.PlainError.Describe(ex, "Google")
                + " The details are copied.", problem: true);
        }
    }

    private async void OnCopySigningInfoClicked(object sender, EventArgs e)
    {
        await Clipboard.SetTextAsync(SigningInfoText());
        CopyStatusLabel.Text = "Copied.";
    }

    // There is no Save (DESIGN.md §5, audit W8.4): each field is stored as it is typed, and the vault
    // publish runs when a field loses focus or the page goes away (leaving by a tab never blurs a field).
    private readonly HashSet<string> unpublished = [];

    private void OnFieldChanged(object? sender, TextChangedEventArgs e)
    {
        var text = e.NewTextValue?.Trim() ?? string.Empty;
        if (sender == VaultPassphraseEntry)
        {
            SettingsStore.VaultPassphrase = e.NewTextValue ?? string.Empty;
            return;
        }

        // A blank never overwrites the stored key.
        if (text.Length == 0)
            return;

        // Only what the owner changed here may replace the vault's copy (VaultMerge.Pick).
        if (sender == ClaudeKeyEntry && text != SettingsStore.ClaudeApiKey)
        {
            SettingsStore.ClaudeApiKey = text;
            unpublished.Add(FolioVaultSection.Claude);
        }
        else if (sender == GeminiKeyEntry && text != SettingsStore.GeminiApiKey)
        {
            SettingsStore.GeminiApiKey = text;
            unpublished.Add(FolioVaultSection.Gemini);
        }
    }

    private void OnFieldSettled(object? sender, FocusEventArgs e) =>
        _ = PublishChangedAsync();

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _ = PublishChangedAsync();
    }

    // Publishes this app's keys (and, since the shared Claude key is entered here, the voice key) to the
    // encrypted Drive vault so every com.storyworthtelling app can back up and restore from one place.
    // The keys already work locally; a failed publish surfaces instead of vanishing.
    private async Task PublishChangedAsync()
    {
        if (unpublished.Count == 0)
            return;

        var changed = unpublished.ToList();
        unpublished.Clear();
        await PublishToVaultAsync(changed);
    }

    // Publishes and, only when a passphrase was set but the backup didn't land, tells the owner. A
    // no-passphrase skip stays quiet (the app just isn't in the vault yet).
    private async Task PublishToVaultAsync(IReadOnlyCollection<string> changed)
    {
        SecretsVaultPublishResult result;
        try
        {
            var sync = IPlatformApplication.Current!.Services.GetService<VaultSync>();
            if (sync is null)
                return;
            result = await sync.PublishAsync(changed, CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = SecretsVaultPublishResult.WriteFailed(ex.Message);
        }
        if (result.NeedsAttention)
        {
            // Behind the fold, here and on the page saving returns to (audit W8.2b: it was an alert).
            var notice = new DogEarNotice("The vault backup did not finish.", result.Message, DogEarHue.Problem);
            SetNotice(VaultBackupNotice, notice);
            CarriedNotices.Shared.Carry(VaultBackupNotice, notice);
        }
    }

    // Both toggles save on change — the setting is read live by the worker, so there is nothing to confirm
    // and no success message to show (DESIGN.md §5: success is the state change itself).
    private void OnArchiveToggled(object? sender, ToggledEventArgs e)
    {
        SettingsStore.ArchiveToDrive = e.Value;
        FreeSpaceSwitch.IsEnabled = e.Value;
        // Never leave "free up space" armed while archiving is off: that combination would delete the
        // intermediates without anything having been uploaded.
        if (!e.Value && FreeSpaceSwitch.IsToggled)
        {
            FreeSpaceSwitch.IsToggled = false;
            SettingsStore.FreeLocalCopiesAfterArchive = false;
        }
    }

    private void OnFreeSpaceToggled(object? sender, ToggledEventArgs e) =>
        SettingsStore.FreeLocalCopiesAfterArchive = e.Value;

    private void OnRemarkableToggled(object? sender, ToggledEventArgs e) =>
        SettingsStore.DeliverToRemarkable = e.Value;

    // One-time device-code pairing via the shared library. Stores the returned device token and backs it up
    // to the vault so a factory reset (or a sibling app) restores it without re-pairing.
    private async void OnRegisterRemarkableClicked(object sender, EventArgs e)
    {
        var code = await PaperCards.AskText(this, "Pair the reMarkable",
            "Open my.remarkable.com/device/mobile/connect and enter its one-time code", "e.g. abcdefgh", null, "Pair",
            text => text.Length == 0 ? "Enter the code the page shows." : null);
        if (code is null)
            return;

        var pairing = PairingTrail.Start();
        try
        {
            var registration = IPlatformApplication.Current!.Services
                .GetRequiredService<Storyworthtelling.Remarkable.IRemarkableRegistration>();
            SettingsStore.RemarkableDeviceToken = await registration.Register(code);
            PairingTrail.Paired(pairing);
            UpdateRemarkableStatus();
            await PublishToVaultAsync([FolioVaultSection.Remarkable]);
        }
        catch (HttpRequestException ex)
        {
            PairingTrail.Failed(pairing, ex);
            Say(RemarkableStatusLabel,
                $"The reMarkable cloud did not accept that code ({(int?)ex.StatusCode}). Check it and pair again.", problem: true);
        }
        catch (Exception ex)
        {
            PairingTrail.Failed(pairing, ex);
            Say(RemarkableStatusLabel, "Pairing did not finish: " + Storyworthtelling.Diagnostics.PlainError.Describe(ex, "reMarkable"), problem: true);
        }
    }

    // The reMarkable pairing mirrors the credential-field rule: it appears only when it needs doing (never
    // paired, or the tablet turned the pairing away), and a working one is not on the screen (audit W8.4b).
    // The delivery toggle is a real choice, so it stays; unpaired, it is off and disabled.
    private void UpdateRemarkableStatus()
    {
        var paired = !string.IsNullOrEmpty(SettingsStore.RemarkableDeviceToken);
        // The pairing's health, not the token's presence (audit 11 §11).
        var pairing = Storyworthtelling.Remarkable.RemarkablePairingStatus.Describe(
            paired,
            Storyworthtelling.Maui.Health.MauiCredentialHealth.Store().Load().Get(Storyworthtelling.Remarkable.RemarkableCredentialId.Value),
            "Not registered yet.");
        RemarkableStatusLabel.Text = pairing.Line;
        RemarkablePairingBlock.IsVisible = pairing.NeedsPairing;
        RemarkableSwitch.IsEnabled = paired;
        if (!paired && RemarkableSwitch.IsToggled)
        {
            RemarkableSwitch.IsToggled = false;
            SettingsStore.DeliverToRemarkable = false;
        }
    }

    // LOG-LOOP-PLAN.md §3.3: Send logs now, a declared key action. Success is the status line changing (§7).
    private async void OnSendLogsNowClicked(object? sender, EventArgs e)
    {
        var sending = MauiTapWatch.Action(FolioKeyActions.SendLogsNow, TimeSpan.FromMinutes(2));
        var result = await MauiLogLoop.UploadAsync(CancellationToken.None);
        if (result.Succeeded)
        {
            sending.Succeeded();
        }
        else
        {
            sending.Failed(result.Error ?? result.Refusal?.ToString() ?? "not uploaded");
        }
        DiagnosticsStatusLabel.Text = result.Succeeded
            ? $"Logs sent: {result.Confirmed} of {result.Planned} batches confirmed in Drive."
            : MauiLogLoop.Status ?? $"Logs not sent: {result.Error}";
    }
}
