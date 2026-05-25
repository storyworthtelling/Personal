using Microsoft.Extensions.DependencyInjection;
using NytRemarkable.Infrastructure.Delivery;

namespace NytRemarkable.Android.Pages;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StatusLabel.Text = CredentialStore.LastDeliveryResult;
        ConfigWarningLabel.IsVisible = !CredentialStore.IsConfigured;
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
}
