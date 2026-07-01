using Storyboarder.Android.Google;

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
            await DisplayAlert("Google", $"Signed in as {email}.", "OK");
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
        await DisplayAlert("Copied", "Package name and SHA-1 copied to the clipboard.", "OK");
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        SettingsStore.ClaudeApiKey = ClaudeKeyEntry.Text?.Trim() ?? string.Empty;
        SettingsStore.GeminiApiKey = GeminiKeyEntry.Text?.Trim() ?? string.Empty;
        await DisplayAlert("Saved", "Settings saved.", "OK");
        await Navigation.PopAsync();
    }
}
