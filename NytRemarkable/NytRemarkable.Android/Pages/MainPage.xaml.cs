using Microsoft.Extensions.DependencyInjection;
using NytRemarkable.Infrastructure.Delivery;

namespace NytRemarkable.Android.Pages;

public partial class MainPage : ContentPage
{
    private static string CrashLogPath =>
        System.IO.Path.Combine(FileSystem.AppDataDirectory, "crash.log");

    private static string DeliveryLogPath =>
        System.IO.Path.Combine(FileSystem.AppDataDirectory, "delivery.log");

    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StatusLabel.Text = CredentialStore.LastDeliveryResult;
        ConfigWarningLabel.IsVisible = !CredentialStore.IsConfigured;
        DeliveryLogButton.IsVisible = System.IO.File.Exists(DeliveryLogPath) &&
                                      new System.IO.FileInfo(DeliveryLogPath).Length > 0;
        CrashLogButton.IsVisible = System.IO.File.Exists(CrashLogPath);

        var batteryOptimized = IsBatteryOptimized();
        BatteryWarningLabel.IsVisible = batteryOptimized;
        BatterySettingsButton.IsVisible = batteryOptimized;
    }

    private static bool IsBatteryOptimized()
    {
        var pm = global::Android.App.Application.Context
            .GetSystemService(global::Android.Content.Context.PowerService)
            as global::Android.OS.PowerManager;
        return pm != null &&
               !pm.IsIgnoringBatteryOptimizations(global::Android.App.Application.Context.PackageName);
    }

    private void OnBatterySettingsClicked(object sender, EventArgs e)
    {
        var intent = new global::Android.Content.Intent(
            global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
        intent.SetData(global::Android.Net.Uri.Parse(
            $"package:{global::Android.App.Application.Context.PackageName}"));
        Platform.CurrentActivity!.StartActivity(intent);
    }

    private async void OnDeliverNowClicked(object sender, EventArgs e)
    {
        if (!CredentialStore.IsConfigured)
        {
            await DisplayAlert("Not Configured",
                "Please enter your credentials in Settings first.", "OK");
            return;
        }

        DeliveryFileLogger.Clear();
        DeliverNowButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        StatusLabel.Text = "Delivering…";

        try
        {
            await Task.Run(async () =>
            {
                using var scope = IPlatformApplication.Current!.Services.CreateScope();
                var delivery = scope.ServiceProvider.GetRequiredService<CrosswordDeliveryService>();
                var today = DateOnly.FromDateTime(DateTime.Today);
                var newsStatus = await delivery.DeliverDailyPuzzle(today);

                if (CrosswordDeliveryService.IsFirstSundayOfMonth(today))
                    await delivery.DeliverBonusPuzzle(today);

                CredentialStore.LastDeliveryResult = $"Success — {today:MMM d, yyyy} ({newsStatus})";
            });

            StatusLabel.Text = CredentialStore.LastDeliveryResult;
            DeliveryLogButton.IsVisible = true;
            await DisplayAlert("Done", CredentialStore.LastDeliveryResult, "OK");
        }
        catch (Exception ex)
        {
            var fullMessage = FlattenException(ex);
            CredentialStore.LastDeliveryResult = $"Failed — {DateTime.Today:MMM d}: {ex.Message}";
            StatusLabel.Text = CredentialStore.LastDeliveryResult;
            DeliveryLogButton.IsVisible = true;
            await Clipboard.SetTextAsync(fullMessage);
            await DisplayAlert("Delivery Failed (copied to clipboard)", fullMessage, "OK");
        }
        finally
        {
            Spinner.IsRunning = false;
            Spinner.IsVisible = false;
            DeliverNowButton.IsEnabled = true;
        }
    }

    private static string FlattenException(Exception ex)
    {
        var parts = new System.Text.StringBuilder();
        var current = ex;
        while (current != null)
        {
            parts.AppendLine(current.Message);
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
                parts.AppendLine(current.StackTrace);
            current = current.InnerException;
            if (current != null) parts.AppendLine("Caused by:");
        }
        return parts.ToString().Trim();
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(
            IPlatformApplication.Current!.Services.GetRequiredService<SetupPage>());
    }

    private async void OnDeliveryLogClicked(object sender, EventArgs e)
    {
        if (!System.IO.File.Exists(DeliveryLogPath))
        {
            await DisplayAlert("Delivery Log", "No delivery log found.", "OK");
            return;
        }

        var log = System.IO.File.ReadAllText(DeliveryLogPath);
        if (string.IsNullOrWhiteSpace(log))
        {
            await DisplayAlert("Delivery Log", "Log is empty.", "OK");
            return;
        }

        var action = await DisplayActionSheet("Delivery Log", "Cancel", null, "View & Copy", "Share");
        if (action == "View & Copy")
        {
            var display = log.Length > 3000 ? "(truncated — use Share for full log)\n\n" + log[^3000..] : log;
            await Clipboard.SetTextAsync(log);
            await DisplayAlert("Delivery Log (copied to clipboard)", display, "OK");
        }
        else if (action == "Share")
        {
            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "NytRemarkable Delivery Log",
                File = new ShareFile(DeliveryLogPath)
            });
        }
    }

    private async void OnCrashLogClicked(object sender, EventArgs e)
    {
        if (!System.IO.File.Exists(CrashLogPath))
        {
            await DisplayAlert("Crash Log", "No crash log found.", "OK");
            return;
        }

        var log = System.IO.File.ReadAllText(CrashLogPath);

        var action = await DisplayActionSheet("Crash Log", "Cancel", null, "View & Copy", "Share", "Clear");
        if (action == "View & Copy")
        {
            var display = log.Length > 3000 ? "(truncated — use Share for full log)\n\n" + log[^3000..] : log;
            await Clipboard.SetTextAsync(log);
            await DisplayAlert("Crash Log (copied to clipboard)", display, "OK");
        }
        else if (action == "Share")
        {
            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "NytRemarkable Crash Log",
                File = new ShareFile(CrashLogPath)
            });
        }
        else if (action == "Clear")
        {
            System.IO.File.Delete(CrashLogPath);
            CrashLogButton.IsVisible = false;
        }
    }
}
