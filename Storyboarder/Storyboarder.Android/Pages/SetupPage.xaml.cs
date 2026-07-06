using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Storyboarder.Android.Google;
using Storyboarder.Android.Platforms.Android;
using Storyboarder.Infrastructure.Services;

namespace Storyboarder.Android.Pages;

public partial class SetupPage : ContentPage
{
    public SetupPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ClaudeKeyEntry.Text = SettingsStore.ClaudeApiKey;
        GeminiKeyEntry.Text = SettingsStore.GeminiApiKey;
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
}
