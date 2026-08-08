using Microsoft.Extensions.Logging;
using Ledger.Domain.External;
using Ledger.Infrastructure.Services;

namespace Ledger.Android.Pages;

// Connects an institution via Plaid Hosted Link in a WebView. The app creates the link token and
// hosts the returned URL; when the user finishes, "Done" reads the completed session's public_token
// (/link/token/get), exchanges it for a durable access token, and records the institution.
//
// Every step also writes to the shared refresh log (viewable via Settings → "Share refresh log") so a
// blank screen or a stalled connect can be diagnosed off-device: whether the link token was created,
// whether Plaid returned a hosted URL, and whether the WebView actually loaded it. Only lengths and
// host names are logged — never the secret or the token values.
public partial class PlaidLinkPage : ContentPage
{
    private readonly IPlaidClient plaid;
    private readonly AccountService accounts;
    private readonly ILogger<PlaidLinkPage> logger;

    private string? linkToken;
    private bool finishing;

    public PlaidLinkPage(IPlaidClient plaid, AccountService accounts, ILogger<PlaidLinkPage> logger)
    {
        InitializeComponent();
        this.plaid = plaid;
        this.accounts = accounts;
        this.logger = logger;
        LinkWebView.Navigating += OnWebViewNavigating;
        LinkWebView.Navigated += OnWebViewNavigated;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (linkToken is not null)
            return; // already prepared

        logger.LogInformation(
            "Connect: creating link token (environment={Environment}, configured={Configured}).",
            SettingsStore.PlaidEnvironment, SettingsStore.IsConfigured);

        try
        {
            var packageName = global::Android.App.Application.Context.PackageName ?? "com.storyworthtelling.ledger";
            var token = await plaid.CreateLinkTokenAsync(packageName, CancellationToken.None);
            linkToken = token.LinkToken;
            logger.LogInformation(
                "Connect: link token created (tokenLength={Length}, hasHostedUrl={HasUrl}).",
                token.LinkToken?.Length ?? 0, !string.IsNullOrEmpty(token.HostedLinkUrl));

            if (string.IsNullOrEmpty(token.HostedLinkUrl))
            {
                logger.LogWarning("Connect: Plaid returned no hosted_link_url; cannot open Link.");
                StatusLabel.Text = "Couldn't open Plaid Link. Check your credentials and environment in Settings.";
                return;
            }

            logger.LogInformation("Connect: loading hosted link (host={Host}).", HostOf(token.HostedLinkUrl));
            LinkWebView.Source = token.HostedLinkUrl;
            StatusLabel.Text = "Sign in to your institution above, then tap Done.";
            DoneButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Connect: failed to start Plaid Link.");
            StatusLabel.Text = $"Couldn't start Plaid Link: {ex.Message}";
        }
    }

    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e) =>
        logger.LogInformation("Connect: WebView navigating (host={Host}).", HostOf(e.Url));

    private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result == WebNavigationResult.Success)
            logger.LogInformation("Connect: WebView loaded (host={Host}).", HostOf(e.Url));
        else
            logger.LogWarning("Connect: WebView load did not succeed (host={Host}, result={Result}).",
                HostOf(e.Url), e.Result);
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        if (finishing || linkToken is null)
            return;
        finishing = true;
        DoneButton.IsEnabled = false;
        StatusLabel.Text = "Finishing up…";

        try
        {
            var result = await plaid.GetLinkResultAsync(linkToken, CancellationToken.None);
            if (result is null)
            {
                logger.LogInformation("Connect: no completed Link session yet; waiting for the user to finish.");
                StatusLabel.Text = "Finish connecting in the page above, then tap Done.";
                DoneButton.IsEnabled = true;
                finishing = false;
                return;
            }

            var access = await plaid.ExchangePublicTokenAsync(result.PublicToken, CancellationToken.None);
            var name = result.InstitutionName ?? access.InstitutionName ?? "New account";
            await accounts.AddInstitutionAsync(access.ItemId, access.AccessToken, name, CancellationToken.None);
            logger.LogInformation("Connect: connected institution {Institution}.", name);

            // Kick an immediate refresh so balances appear without waiting for the daily cycle.
            RefreshWorker.RunNow(global::Android.App.Application.Context!);
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Connect: failed to finish connecting.");
            StatusLabel.Text = $"Couldn't finish connecting: {ex.Message}";
            DoneButton.IsEnabled = true;
            finishing = false;
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await Navigation.PopAsync();

    private static string HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "(none)";
}
