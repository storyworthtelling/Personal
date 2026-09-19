using Android.Content;
using Android.Content.PM;
using Microsoft.Extensions.Logging;
using Ledger.Domain.Connect;
using Ledger.Domain.External;
using Ledger.Domain.Pipeline;
using Ledger.Infrastructure.Services;
using Storyworthtelling.Voice.Secrets;

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
public partial class PlaidLinkPage : LedgerBinderPage
{
    private readonly IPlaidClient plaid;
    private readonly AccountService accounts;
    private readonly VaultSync vaultSync;
    private readonly ILogger<PlaidLinkPage> logger;

    private readonly Button doneButton = new() { Text = "Done", IsEnabled = false };

    // Plaid populates the Hosted Link session's public_token a few seconds after the OAuth redirect
    // returns (worst with app-to-app banks like Chase). Poll a short window with exponential backoff so
    // the connection lands without the owner having to guess when to tap — ~1+2+4+8+8s across six reads.
    private const int ConnectPollAttempts = 6;
    private static BackoffScheduler ConnectPollBackoff() =>
        new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8));

    private string? linkToken;
    private string? hostedLinkUrl;
    private bool opened;
    private bool finishing;

    public PlaidLinkPage(IPlaidClient plaid, AccountService accounts, VaultSync vaultSync, ILogger<PlaidLinkPage> logger)
    {
        InitializeComponent();
        // Reached only from Settings ("Connect an account"), so it lights that tab.
        Wrap(LedgerArea.Settings, "Connect an account");
        doneButton.Clicked += OnDoneClicked;
        // Application.Current.Resources, not this page's own (empty) Resources dictionary — a
        // page's own indexer doesn't walk what's merged into the app, only what it declares itself.
        var cancelButton = new Button { Text = "Cancel", Style = (Style)Application.Current!.Resources["QuietButton"] };
        cancelButton.Clicked += OnCancelClicked;
        SetBar(doneButton, cancelButton);
        this.plaid = plaid;
        this.accounts = accounts;
        this.vaultSync = vaultSync;
        this.logger = logger;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (linkToken is not null)
        {
            // Returning from the browser: the token is already prepared (don't recreate it or reopen the
            // tab). Auto-detect the completed session — the owner shouldn't have to tap Done or guess when
            // Plaid has caught up. Done stays a manual retry for the case this poll returns empty.
            if (opened && !finishing)
                await TryFinishAsync(auto: true);
            return;
        }

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
            doneButton.IsEnabled = true;
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

    private async void OnDoneClicked(object? sender, EventArgs e) => await TryFinishAsync(auto: false);

    // Reads the completed Link session and records the institution. Shared by the automatic poll on
    // return from the browser (auto: true) and the Done button as a manual retry (auto: false). Polls
    // /link/token/get with backoff so Chase's post-OAuth propagation lag no longer drops the connection.
    private async Task TryFinishAsync(bool auto)
    {
        if (finishing || linkToken is null)
            return;

        if (!opened)
        {
            StatusLabel.Text = "Tap “Open connection page” and finish in the browser first.";
            return;
        }

        finishing = true;
        doneButton.IsEnabled = false;
        StatusLabel.Text = auto ? "Checking for your connection…" : "Finishing up…";

        try
        {
            logger.LogInformation(
                "Connect: polling for the completed Link session (auto={Auto}, maxAttempts={Attempts}).",
                auto, ConnectPollAttempts);
            var result = await plaid.PollLinkResultAsync(
                linkToken, ConnectPollAttempts, ConnectPollBackoff(), delay: null, CancellationToken.None);
            if (result is null)
            {
                logger.LogInformation(
                    "Connect: no completed Link session after {Attempts} attempts; awaiting a retry.",
                    ConnectPollAttempts);
                StatusLabel.Text =
                    "Couldn't find your connection yet. Finish signing in in the browser, then tap Done to " +
                    "retry — or try connecting on a different phone.";
                doneButton.IsEnabled = true;
                finishing = false;
                return;
            }

            var access = await plaid.ExchangePublicTokenAsync(result.PublicToken, CancellationToken.None);
            var name = result.InstitutionName ?? access.InstitutionName ?? "New account";
            await accounts.AddInstitutionAsync(access.ItemId, access.AccessToken, name, CancellationToken.None);
            // A single login can carry several cards (e.g. a Chase login covers both the Amazon and United
            // cards). They arrive as separate accounts under this one Item on the next refresh.
            logger.LogInformation("Connect: connected institution {Institution}; refreshing its accounts.", name);

            // Back up the new bank's access token to the encrypted vault so a later reset can restore it
            // without re-linking. The connection already works locally, but surface a failed backup so a
            // silently un-saved token doesn't quietly force a re-link after the next reset.
            SecretsVaultPublishResult backup;
            try { backup = await vaultSync.PublishAsync(CancellationToken.None); }
            catch (Exception ex) { backup = SecretsVaultPublishResult.WriteFailed(ex.Message); }
            if (backup.NeedsAttention)
                await DisplayAlert("Vault backup didn't complete", backup.Message, "OK");

            // Kick an immediate refresh so balances appear without waiting for the daily cycle.
            RefreshWorker.RunNow(global::Android.App.Application.Context!);
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Connect: failed to finish connecting.");
            StatusLabel.Text = $"Couldn't finish connecting: {ex.Message}";
            doneButton.IsEnabled = true;
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
