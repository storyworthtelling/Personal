using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace DailyLoader.Android.Pages;

public partial class SetupPage : ContentPage
{
    public SetupPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        NytCookieEntry.Text = CredentialStore.NytSessionCookie;
        NytApiKeyEntry.Text = CredentialStore.NytApiKey;
        RemarkableTokenEntry.Text = CredentialStore.RemarkableDeviceToken;
        AnthropicKeyEntry.Text = CredentialStore.AnthropicApiKey;
        CalendarMappingEditor.Text = CredentialStore.CalendarMappingJson;
        VaultPassphraseEntry.Text = CredentialStore.VaultPassphrase;
        GoogleStatusLabel.Text = CredentialStore.IsGoogleConfigured
            ? $"Signed in as {CredentialStore.GoogleAccountEmail} ✓"
            : "Not signed in";
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        CredentialStore.NytSessionCookie = NytCookieEntry.Text?.Trim() ?? string.Empty;
        CredentialStore.NytApiKey = NytApiKeyEntry.Text?.Trim() ?? string.Empty;
        CredentialStore.RemarkableDeviceToken = RemarkableTokenEntry.Text?.Trim() ?? string.Empty;
        CredentialStore.AnthropicApiKey = AnthropicKeyEntry.Text?.Trim() ?? string.Empty;
        CredentialStore.CalendarMappingJson = CalendarMappingEditor.Text?.Trim() ?? string.Empty;
        CredentialStore.VaultPassphrase = VaultPassphraseEntry.Text ?? string.Empty;

        // Back up the credentials to the encrypted Drive vault. Best-effort: they already work locally, so a
        // failed publish (no passphrase/network/scope yet) is silent.
        PublishToVault();
        // Success is the state change: saving returns home (DESIGN.md §7).
        await Navigation.PopAsync();
    }

    private static async void PublishToVault()
    {
        try
        {
            var sync = IPlatformApplication.Current!.Services.GetService<VaultSync>();
            if (sync is not null)
                await sync.PublishAsync(CancellationToken.None);
        }
        catch
        {
            // Best-effort: the vault picks the credentials up on the next successful save.
        }
    }

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
            var email = await DailyLoader.Android.Google.DeviceAccountSignIn.SignIn(activity);
            GoogleStatusLabel.Text = $"Signed in as {email} ✓";
            // The status line below the button is the state change.
            GoogleStatusLabel.Text = $"Signed in as {email}.";
        }
        catch (Exception ex)
        {
            var fullMessage = FlattenException(ex);
            GoogleStatusLabel.Text = "Sign-in failed";
            await Clipboard.SetTextAsync(fullMessage);
            await DisplayAlert("Google Sign-in Failed (copied to clipboard)", fullMessage, "OK");
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

    private async void OnRegisterRemarkableClicked(object sender, EventArgs e)
    {
        var code = await DisplayPromptAsync(
            "Register reMarkable",
            "Visit my.remarkable.com/device/mobile/connect and enter the one-time code:",
            placeholder: "e.g. abcdefgh");

        if (string.IsNullOrWhiteSpace(code)) return;

        try
        {
            using var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());
            var body = JsonSerializer.Serialize(new
            {
                code = code.Trim(),
                deviceDesc = "mobile-android",
                deviceID = Guid.NewGuid().ToString()
            });

            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://webapp-prod.cloud.remarkable.engineering/token/json/2/device/new");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                await DisplayAlert("Registration Failed",
                    $"Server returned {(int)response.StatusCode}. Check your code and try again.", "OK");
                return;
            }

            var token = (await response.Content.ReadAsStringAsync()).Trim().Trim('"');
            CredentialStore.RemarkableDeviceToken = token;
            RemarkableTokenEntry.Text = token;
            // Back up the freshly paired token so a later reset doesn't force re-registration.
            PublishToVault();
            await DisplayAlert("Registered!", "Device registered and token saved.", "OK");
        }
        catch (Exception ex)
        {
            var fullMessage = FlattenException(ex);
            await Clipboard.SetTextAsync(fullMessage);
            await DisplayAlert("Error (copied to clipboard)", fullMessage, "OK");
        }
    }
}
