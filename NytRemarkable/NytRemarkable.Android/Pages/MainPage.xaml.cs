using Microsoft.Extensions.DependencyInjection;
using NytRemarkable.Infrastructure.Delivery;

namespace NytRemarkable.Android.Pages;

public partial class MainPage : ContentPage
{
    private static string CrashLogPath =>
        System.IO.Path.Combine(FileSystem.AppDataDirectory, "crash.log");

    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StatusLabel.Text = CredentialStore.LastDeliveryResult;
        ConfigWarningLabel.IsVisible = !CredentialStore.IsConfigured;
        CrashLogButton.IsVisible = System.IO.File.Exists(CrashLogPath);
    }

    private async void OnDeliverNowClicked(object sender, EventArgs e)
    {
        if (!CredentialStore.IsConfigured)
        {
            await DisplayAlert("Not Configured",
                "Please enter your credentials in Settings first.", "OK");
            return;
        }

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
                await delivery.DeliverDailyPuzzle(today);

                if (CrosswordDeliveryService.IsFirstSundayOfMonth(today))
                    await delivery.DeliverBonusPuzzle(today);

                CredentialStore.LastDeliveryResult = $"Success — {today:MMM d, yyyy}";
            });

            StatusLabel.Text = CredentialStore.LastDeliveryResult;
            await DisplayAlert("Done", "Crossword delivered to your reMarkable!", "OK");
        }
        catch (Exception ex)
        {
            CredentialStore.LastDeliveryResult = $"Failed — {DateTime.Today:MMM d}: {ex.Message}";
            StatusLabel.Text = CredentialStore.LastDeliveryResult;
            await DisplayAlert("Delivery Failed", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsRunning = false;
            Spinner.IsVisible = false;
            DeliverNowButton.IsEnabled = true;
        }
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(
            IPlatformApplication.Current!.Services.GetRequiredService<SetupPage>());
    }

    private async void OnCrashLogClicked(object sender, EventArgs e)
    {
        if (!System.IO.File.Exists(CrashLogPath))
        {
            await DisplayAlert("Crash Log", "No crash log found.", "OK");
            return;
        }

        var log = System.IO.File.ReadAllText(CrashLogPath);

        var action = await DisplayActionSheet("Crash Log", "Cancel", null, "View", "Share", "Clear");
        if (action == "View")
        {
            var display = log.Length > 3000 ? "(truncated — use Share for full log)\n\n" + log[^3000..] : log;
            await DisplayAlert("Crash Log", display, "OK");
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
