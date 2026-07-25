using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Storyboarder.Android.Google;
using Storyboarder.Android.Platforms.Android;
using Storyboarder.Infrastructure.Services;

namespace Storyboarder.Android.Pages;

public partial class SetupPage : ContentPage
{
    private static string PipelineLogPath => System.IO.Path.Combine(FileSystem.AppDataDirectory, "pipeline.log");
    private static string CrashLogPath => System.IO.Path.Combine(FileSystem.AppDataDirectory, "crash.log");

    public SetupPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ClaudeKeyEntry.Text = SettingsStore.ClaudeApiKey;
        GeminiKeyEntry.Text = SettingsStore.GeminiApiKey;
        ArchiveSwitch.IsToggled = SettingsStore.ArchiveToDrive;
        FreeSpaceSwitch.IsToggled = SettingsStore.FreeLocalCopiesAfterArchive;
        // Reclaiming space only means anything once the copies are safely elsewhere.
        FreeSpaceSwitch.IsEnabled = ArchiveSwitch.IsToggled;
        UpdateGoogleStatus();
        SigningInfoLabel.Text = SigningInfoText();

        var migrationError = SettingsStore.LastMigrationError;
        MigrationErrorCard.IsVisible = !string.IsNullOrEmpty(migrationError);
        if (MigrationErrorCard.IsVisible)
            MigrationErrorLabel.Text =
                "The last app update couldn't finish updating your library, so work is paused. " +
                "Restoring a recent backup usually fixes it. Details: " + migrationError;
    }

    private static ProjectBackupService BackupService() =>
        IPlatformApplication.Current!.Services.GetRequiredService<ProjectBackupService>();

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
            var suggestedName = $"storyboarder-backup-{DateTime.Now:yyyyMMdd-HHmm}.zip";
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
            BackupStatusLabel.Text = "Backup failed: " + ex.Message;
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

        var confirm = await DisplayAlert(
            "Restore from a backup",
            "This replaces the current library with the backup you pick. Any progress not in that backup will be lost.",
            "Choose a backup", "Cancel");
        if (!confirm)
            return;

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
            PipelineWorker.Schedule(activity);
            PipelineWorker.RunNow(activity);
            BackupStatusLabel.Text = "Restored. Your conversion will resume shortly.";
        }
        catch (Exception ex)
        {
            BackupStatusLabel.Text = "Restore failed: " + ex.Message;
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
            await DisplayAlert("Status snapshot", "Couldn't build the snapshot: " + ex.Message, "OK");
            return;
        }

        var action = await DisplayActionSheet("Status snapshot", "Cancel", null, "View & copy", "Share");
        if (action == "View & copy")
        {
            var preview = report.Length > 3000 ? report[..3000] + "\n\n(truncated — use Share for the full snapshot)" : report;
            await Clipboard.SetTextAsync(report);
            await DisplayAlert("Status snapshot (copied to clipboard)", preview, "OK");
        }
        else if (action == "Share")
        {
            await ShareText("storyboarder-status.txt", report, "Storyboarder status snapshot");
        }
    }

    // A focused capture of the panels a content filter stopped — including the exact prompt sent to
    // Gemini — so a prohibited/IP block can be diagnosed and its art direction reworked. Shared as a file
    // (never copied to the clipboard) so a long provider response can't trip the Binder size limit.
    private async void OnBlockedPanelReportClicked(object sender, EventArgs e)
    {
        try
        {
            using var scope = IPlatformApplication.Current!.Services.CreateScope();
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            var report = await projects.BuildBlockedPanelReportAsync(CancellationToken.None);
            await ShareText($"storyboarder-blocked-panels-{DateTime.Now:yyyyMMdd-HHmm}.txt", report, "Storyboarder blocked panels");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Blocked panel report", "Couldn't build the report: " + ex.Message, "OK");
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
                .AppendLine("===== PIPELINE LOG =====").AppendLine().AppendLine(ReadOrEmpty(PipelineLogPath)).AppendLine()
                .AppendLine("===== CRASH LOG =====").AppendLine().AppendLine(ReadOrEmpty(CrashLogPath))
                .ToString();
            await ShareText($"storyboarder-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.txt", bundle, "Storyboarder diagnostics");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Export diagnostics", "Couldn't export: " + ex.Message, "OK");
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
        await ShowLog("Pipeline log", PipelineLogPath, PipelineFileLogger.Clear);

    private async void OnCrashLogClicked(object sender, EventArgs e) =>
        await ShowLog("Crash log", CrashLogPath, () => System.IO.File.Delete(CrashLogPath));

    // Shared View & copy / Share / Clear flow for a diagnostic log file (mirrors DailyLoader). The
    // buttons stay visible even when a log is empty, so the crash log is always reachable.
    private async Task ShowLog(string title, string path, Action clear)
    {
        if (!HasContent(path))
        {
            await DisplayAlert(title, $"No {title.ToLowerInvariant()} recorded yet — nothing to show.", "OK");
            return;
        }

        var log = System.IO.File.ReadAllText(path);
        var action = await DisplayActionSheet(title, "Cancel", null, "View & copy", "Share", "Clear");
        if (action == "View & copy")
        {
            var preview = log.Length > 3000 ? "(truncated — use Share for the full log)\n\n" + log[^3000..] : log;
            await Clipboard.SetTextAsync(log);
            await DisplayAlert($"{title} (copied to clipboard)", preview, "OK");
        }
        else if (action == "Share")
        {
            await Share.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path) });
        }
        else if (action == "Clear")
        {
            clear();
            DiagnosticsStatusLabel.Text = $"{title} cleared.";
        }
    }

    private void UpdateGoogleStatus() =>
        GoogleStatusLabel.Text = SettingsStore.IsGoogleConfigured
            ? $"Signed in as {SettingsStore.GoogleAccountEmail} ✓"
            : "Not signed in";

    private static string SigningInfoText() =>
        $"package: {AppSigningInfo.PackageName}\nSHA-1: {AppSigningInfo.Sha1Fingerprint()}";

    private async void OnGoogleSignInClicked(object sender, EventArgs e)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            await DisplayAlert("Google", "Could not start sign-in (no active window).", "OK");
            return;
        }

        try
        {
            var email = await DeviceAccountSignIn.SignIn(activity);
            UpdateGoogleStatus();
            // The status line below the button is the state change.
            GoogleStatusLabel.Text = $"Signed in as {email}.";
        }
        catch (Exception ex) when (ex.ToString().Contains("UnregisteredOnApiConsole", StringComparison.OrdinalIgnoreCase))
        {
            var info = SigningInfoText();
            await Clipboard.SetTextAsync(info);
            await DisplayAlert(
                "This app isn't registered with Google yet",
                "Google rejected sign-in because this app isn't a registered OAuth client.\n\n" +
                "In the Google Cloud Console, create an Android OAuth client with the package and " +
                "SHA-1 below (copied to your clipboard), enable the Google Docs & Drive APIs, and " +
                "add your account as a test user on the consent screen. Then try again.\n\n" + info,
                "OK");
        }
        catch (Exception ex)
        {
            await Clipboard.SetTextAsync(ex.ToString());
            await DisplayAlert("Google sign-in failed (copied to clipboard)", ex.Message, "OK");
        }
    }

    private async void OnCopySigningInfoClicked(object sender, EventArgs e)
    {
        await Clipboard.SetTextAsync(SigningInfoText());
        CopyStatusLabel.Text = "Copied.";
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        SettingsStore.ClaudeApiKey = ClaudeKeyEntry.Text?.Trim() ?? string.Empty;
        SettingsStore.GeminiApiKey = GeminiKeyEntry.Text?.Trim() ?? string.Empty;
        // Success is the state change: saving returns to the library.
        await Navigation.PopAsync();
        await Navigation.PopAsync();
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
}
