using System.Text;
using System.Text.Json;

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
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        CredentialStore.NytSessionCookie = NytCookieEntry.Text?.Trim() ?? string.Empty;
        CredentialStore.NytApiKey = NytApiKeyEntry.Text?.Trim() ?? string.Empty;
        CredentialStore.RemarkableDeviceToken = RemarkableTokenEntry.Text?.Trim() ?? string.Empty;

        await DisplayAlert("Saved", "Settings saved.", "OK");
        await Navigation.PopAsync();
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
