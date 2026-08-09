using Android.Content;
using Android.Content.PM;
using Microsoft.Extensions.Logging;
using Ledger.Domain.Connect;
using Ledger.Domain.External;
using Ledger.Infrastructure.Services;

namespace Ledger.Android.Pages;

// Connects an institution via Plaid Hosted Link. The app creates the link token and opens the returned
// hosted URL in an external browser (an Android Custom Tab), NOT an embedded WebView: OAuth banks such as
// Chase, Bank of America, and Wells Fargo fingerprint the browser environment and refuse to render their
// login inside a WebView, returning a generic "this part of our site isn't working" error. A real browser
// is the only surface those institutions complete in — and it's what Plaid's Hosted Link is designed for.
// When the user finishes in the browser and returns, "Done" reads the completed session's public_token
// (/link/token/get), exchanges it for a durable access token, and records the institution.
//
// Every step also writes to the shared refresh log (viewable via Settings → "Share refresh log") so a
// stalled connect can be diagnosed off-device: whether the link token was created, whether Plaid returned
// a hosted URL, and whether the browser was opened. Only lengths and host names are logged — never the
// secret or the token values.
public partial class PlaidLinkPage : ContentPage
{
    private readonly IPlaidClient plaid;
    private readonly AccountService accounts;
    private readonly VaultSync vaultSync;
    private readonly ILogger<PlaidLinkPage> logger;

    private string? linkToken;
    private string? hostedLinkUrl;
    private bool opened;
    private bool finishing;

    public PlaidLinkPage(IPlaidClient plaid, AccountService accounts, VaultSync vaultSync, ILogger<PlaidLinkPage> logger)
    {
        InitializeComponent();
        this.plaid = plaid;
        this.accounts = accounts;
        this.vaultSync = vaultSync;
        this.logger = logger;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (linkToken is not null)
            return; // already prepared; returning from the browser must not recreate the token or reopen the tab

        // On the FocusPhone the device-owner policy suspends every real browser and pins FocusPhone's own
        // single-domain WebView as the default http/https handler. Opening Plaid Link there dead-ends (a
        // WebView Chase blocks, confined to one domain so the OAuth redirect is refused). Detect that and
        // send the owner to link on a browser-capable phone — the vault restores the token here, and the
        // daily refresh needs no browser. Don't even mint a link token we can't use.
        var defaultBrowser = ResolveDefaultBrowserPackage();
        if (ConnectBrowserPolicy.IsRestrictedViewer(defaultBrowser))
        {
            logger.LogInformation("Connect: default browser is the restricted viewer ({Package}); guiding to link elsewhere.", defaultBrowser);
            InstructionsLabel.Text =
                "This phone can't open a bank's sign-in page. Connect your banks on a phone with a browser — " +
                "Ledger backs up each connection and restores it here automatically. Balances still refresh here " +
                "without a browser.";
            OpenButton.IsVisible = false;
            StatusLabel.Text = "Nothing to do here on this phone.";
            return;
        }

        logger.LogInformation(
            "Connect: creating link token (environment={Environment}, configured={Configured}).",
            SettingsStore.PlaidEnvironment, SettingsStore.IsConfigured);

        try
        {
            var token = await plaid.CreateLinkTokenAsync(CancellationToken.None);
            linkToken = token.LinkToken;
            hostedLinkUrl = token.HostedLinkUrl;
            logger.LogInformation(
                "Connect: link token created (tokenLength={Length}, hasHostedUrl={HasUrl}).",
                token.LinkToken?.Length ?? 0, !string.IsNullOrEmpty(token.HostedLinkUrl));

            if (string.IsNullOrEmpty(hostedLinkUrl))
            {
                logger.LogWarning("Connect: Plaid returned no hosted_link_url; cannot open Link.");
                StatusLabel.Text = "Couldn't open Plaid Link. Check your credentials and environment in Settings.";
                return;
            }

            OpenButton.IsEnabled = true;
            DoneButton.IsEnabled = true;
            await OpenHostedLinkAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Connect: failed to start Plaid Link.");
            StatusLabel.Text = $"Couldn't start Plaid Link: {ex.Message}";
        }
    }

    private async void OnOpenClicked(object? sender, EventArgs e) => await OpenHostedLinkAsync();

    private async Task OpenHostedLinkAsync()
    {
        if (string.IsNullOrEmpty(hostedLinkUrl))
            return;

        try
        {
            logger.LogInformation("Connect: opening hosted link in browser (host={Host}).", HostOf(hostedLinkUrl));
            await Browser.Default.OpenAsync(hostedLinkUrl, BrowserLaunchMode.SystemPreferred);
            opened = true;
            StatusLabel.Text = "Sign in to your institution in the browser, then return here and tap Done.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Connect: failed to open the browser for Plaid Link.");
            StatusLabel.Text = $"Couldn't open the browser: {ex.Message}";
        }
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        if (finishing || linkToken is null)
            return;

        if (!opened)
        {
            StatusLabel.Text = "Tap “Open connection page” and finish in the browser first.";
            return;
        }

        finishing = true;
        DoneButton.IsEnabled = false;
        StatusLabel.Text = "Finishing up…";

        try
        {
            var result = await plaid.GetLinkResultAsync(linkToken, CancellationToken.None);
            if (result is null)
            {
                logger.LogInformation("Connect: no completed Link session yet; waiting for the user to finish.");
                StatusLabel.Text = "Finish connecting in the browser, then tap Done.";
                DoneButton.IsEnabled = true;
                finishing = false;
                return;
            }

            var access = await plaid.ExchangePublicTokenAsync(result.PublicToken, CancellationToken.None);
            var name = result.InstitutionName ?? access.InstitutionName ?? "New account";
            await accounts.AddInstitutionAsync(access.ItemId, access.AccessToken, name, CancellationToken.None);
            logger.LogInformation("Connect: connected institution {Institution}.", name);

            // Back up the new bank's access token to the encrypted vault so a later reset can restore it
            // without re-linking. Best-effort — the connection already works locally.
            try { await vaultSync.PublishAsync(CancellationToken.None); }
            catch { /* Best-effort backup. */ }

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

    // The package that would handle an https link — the phone's default browser. On the FocusPhone this
    // resolves to the launcher's restricted viewer (the pinned preferred activity); on a normal phone it's
    // Chrome or whatever the owner set. Returns null if nothing resolves, which the policy treats as
    // "not restricted" so a browser-capable phone is never falsely blocked. Requires the <queries> entry
    // in the manifest for package visibility on Android 11+.
    private string? ResolveDefaultBrowserPackage()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            using var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse("https://example.com"));
            intent.AddCategory(Intent.CategoryBrowsable);
            var resolved = context.PackageManager?.ResolveActivity(intent, PackageInfoFlags.MatchDefaultOnly);
            return resolved?.ActivityInfo?.PackageName;
        }
        catch (Exception ex)
        {
            // Detection is best-effort; a failure here must not stop a normal phone from connecting.
            logger.LogWarning(ex, "Connect: could not resolve the default browser; proceeding.");
            return null;
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await Navigation.PopAsync();

    private static string HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "(none)";
}
