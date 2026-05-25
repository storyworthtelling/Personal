using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NytRemarkable.Android.Pages;

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

        await DisplayAlert("Saved", "Settings saved. Restart the app to apply changes.", "OK");
        await Navigation.PopAsync();
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
            using var http = new HttpClient();
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
            RemarkableTokenEntry.Text = token;
            await DisplayAlert("Registered!", "Device registered successfully. Tap Save to store the token.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }
}
