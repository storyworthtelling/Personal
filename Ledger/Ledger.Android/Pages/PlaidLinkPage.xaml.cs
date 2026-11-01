using Index.Infrastructure.External;
using Storyworthtelling.Diagnostics;
using Index.Domain.Diagnostics;
using Storyworthtelling.Maui.Diagnostics;
using Android.Content;
using Android.Content.PM;
using Microsoft.Extensions.Logging;
using Index.Android.Views;
using Index.Domain.Connect;
using Index.Domain.External;
using Index.Domain.Pipeline;
using Index.Infrastructure.Services;
using Storyworthtelling.Maui;
using Storyworthtelling.Paper;
using Storyworthtelling.Voice.Secrets;

namespace Index.Android.Pages;

// Connects an institution via Plaid Hosted Link (mockup 5). The app creates the link token and sends the
// owner to the hosted page in real Chrome, NOT an embedded WebView: large OAuth banks (the big national
// ones) fingerprint the browser environment and refuse to render their login inside a
// WebView, returning a generic "this part of our site isn't working" error. A real browser is the only
// surface those institutions complete in — and it's what Plaid's Hosted Link is designed for.
//
// The page has three states, written straight onto the graph paper:
//   1. Admin mode needed  — Chrome is suspended. Says why, where Admin mode is, and what doesn't need it.
//                           No button: Index cannot open Flyleaf's settings (D132), so there is no
//                           primary action and therefore no pinned bar.
//   2. Admin mode is open — three steps and one pinned button, "Open Plaid in Chrome". The page never
//                           opens the browser by itself; the button does.
//   3. Finishing up       — Chrome has been opened. The page waits for Plaid to hand the session over
//                           and records the institution. There is nothing to tap to say "I'm back":
//                           returning is enough, because the page looks for a finished session whenever
//                           it reappears. "Check again" is the manual retry, and a quiet line reopens
//                           Chrome.
// Plus a fourth sentence for a phone with no Chrome at all, which is not "Admin mode needed".
//
// How the page knows Admin mode is open: it asks PackageManager whether Chrome is suspended. The default
// browser cannot answer that — on the FocusPhone it stays Flyleaf's restricted viewer even while Admin
// mode is open (R3). The real value of the suspended flag under the lockdown is unverified until it is
// run on the device.
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

    private readonly Button openPlaidButton = new() { Text = ConnectCopy.OpenPlaidButton };
    private readonly Button checkAgainButton = new() { Text = ConnectCopy.CheckAgainButton };
    private readonly Label problemLabel = new() { IsVisible = false };

    // Plaid populates the Hosted Link session's public_token a few seconds after the OAuth redirect
    // returns (worst with app-to-app banks). Poll a short window with exponential backoff so
    // the connection lands without the owner having to guess when to look — ~1+2+4+8+8s across six reads.
    private const int ConnectPollAttempts = 6;
    private static BackoffScheduler ConnectPollBackoff() =>
        new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8));

    private string? linkToken;
    private string? hostedLinkUrl;
    private bool opened;
    private bool finishing;

    // The connect as a sign-in trail (LOG-LOOP-PLAN.md §4.2), written to the log once it ends.
    private SignInTrail? trail;
    private Window? watchedWindow;

    // D150: set by BeginUpdateMode before the page is pushed (this page is registered AddTransient, so
    // a fresh instance answers every navigation — safe to hold per-open state here rather than routing
    // it through the constructor's DI-resolved parameters). Non-null means "repair THIS institution in
    // place", never a fresh Link.
    private Guid? updateInstitutionId;
    private string? updateExistingAccessToken;
    private string? updateInstitutionName;

    public PlaidLinkPage(IPlaidClient plaid, AccountService accounts, VaultSync vaultSync, ILogger<PlaidLinkPage> logger)
    {
        InitializeComponent();
        // Reached from Accounts (the chooser, a reconnect) and from Settings; either way the bank that
        // results lands on Accounts, so that is the tab that is lit.
        Wrap(LedgerArea.Accounts, "Connect a bank");

        Ground.Children.Insert(0, new GraphPaperBackground
        {
            GroundColour = Color.FromArgb(LedgerAreas.AccountsGroundHex),
        });

        // Mockup 5's two bar buttons: the primary is the deep pine, the secondary an outline in the
        // muted ink. Tokens are read through PaperResources, not the indexer (see LedgerBinderPage.SetBar).
        openPlaidButton.BackgroundColor = PaperResources.Colour("AccentDeep");
        openPlaidButton.Clicked += OnOpenPlaidClicked;
        checkAgainButton.Style = PaperResources.Token<Style>("SecondaryButton");
        checkAgainButton.BorderColor = PaperResources.Colour("InkMuted");
        checkAgainButton.BorderWidth = 1.5;
        checkAgainButton.Clicked += OnCheckAgainClicked;
        problemLabel.Style = PaperResources.Token<Style>("ErrorLabel");

        this.plaid = plaid;
        this.accounts = accounts;
        this.vaultSync = vaultSync;
        this.logger = logger;
    }

    // D150: called by MainPage BEFORE pushing this page, to repair an existing institution's broken
    // connection in place rather than starting a fresh Link (the Trial plan's ten-Item cap counts Items
    // CREATED). Must be called before OnAppearing runs — i.e. before Navigation.PushAsync. The name is
    // for the waiting sentence ("Waiting for Plaid to hand over Redwood Bank…"); a fresh link only learns it
    // when Plaid hands the session over.
    public void BeginUpdateMode(Guid institutionId, string existingAccessToken, string institutionName)
    {
        updateInstitutionId = institutionId;
        updateExistingAccessToken = existingAccessToken;
        updateInstitutionName = institutionName;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        WatchWindow();
        await EvaluateAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        UnwatchWindow();
    }

    // Returning to the app is what tells this page that Admin mode was opened (frame 1 → 2) or that
    // Chrome has finished (frame 3 → connected). Page.OnAppearing is not reliably raised when the app comes
    // back to the foreground, so the window's own activation drives the same check; EvaluateAsync is
    // idempotent, so both firing is harmless.
    private void WatchWindow()
    {
        if (Window is null || ReferenceEquals(watchedWindow, Window))
            return;

        UnwatchWindow();
        watchedWindow = Window;
        watchedWindow.Activated += OnWindowActivated;
    }

    private void UnwatchWindow()
    {
        if (watchedWindow is null)
            return;

        watchedWindow.Activated -= OnWindowActivated;
        watchedWindow = null;
    }

    private async void OnWindowActivated(object? sender, EventArgs e) => await EvaluateAsync();

    // Decides which of the states the page is in and shows it. Nothing here opens the browser: a page
    // that sent the owner off the moment it appeared would also do it again on every return.
    private async Task EvaluateAsync()
    {
        if (finishing)
            return;

        if (opened)
        {
            // Chrome has been opened: this is the "back from Chrome" state, whether the page was just
            // re-shown or the owner is looking at it again after a retry.
            ShowFinishingUp();
            await TryFinishAsync(auto: true);
            return;
        }

        var state = ReadAdminMode();
        logger.LogInformation("Connect: Admin mode state is {State}.", state);
        switch (state)
        {
            case AdminModeState.Open:
                ShowAdminModeOpen();
                break;
            case AdminModeState.ChromeMissing:
                ShowChromeMissing();
                break;
            default:
                ShowAdminModeNeeded();
                break;
        }
    }

    // Admin mode, as seen through Chrome: installed, and suspended or not. The member names were read
    // out of Mono.Android's metadata: PackageManager.GetApplicationInfo has a PackageInfoFlags overload
    // and (API 33+) an ApplicationInfoFlags one, ApplicationInfo.Flags carries
    // ApplicationInfoFlags.Suspended, and an absent package throws PackageManager.NameNotFoundException.
    // That exception is also what Android throws when package visibility hides a package, which is why
    // Chrome is declared in the manifest's <queries>.
    private AdminModeState ReadAdminMode()
    {
        try
        {
            var packageManager = global::Android.App.Application.Context.PackageManager
                ?? throw new InvalidOperationException("The application context has no PackageManager.");
            var info = OperatingSystem.IsAndroidVersionAtLeast(33)
                ? packageManager.GetApplicationInfo(
                    ConnectBrowserPolicy.ChromePackage, PackageManager.ApplicationInfoFlags.Of(0))
                : packageManager.GetApplicationInfo(ConnectBrowserPolicy.ChromePackage, (PackageInfoFlags)0);

            var suspended = info is not null && info.Flags.HasFlag(ApplicationInfoFlags.Suspended);
            return ConnectBrowserPolicy.DecideAdminMode(chromeInstalled: info is not null, chromeSuspended: suspended);
        }
        catch (PackageManager.NameNotFoundException ex)
        {
            logger.LogInformation(ex, "Connect: {Package} is not visible to Index.", ConnectBrowserPolicy.ChromePackage);
            return ConnectBrowserPolicy.DecideAdminMode(chromeInstalled: false, chromeSuspended: false);
        }
        catch (Exception ex)
        {
            // The read itself failed (not "no such package"). Claiming Admin mode is open would send the
            // owner to a browser that may be blocked, so the cautious answer is the one that names the
            // way out; the failure is in the log for whoever diagnoses it.
            logger.LogWarning(ex, "Connect: could not read Chrome's state; treating Admin mode as needed.");
            return AdminModeState.Needed;
        }
    }

    // ---- the states ----

    // Frame 1. The page says why, where Admin mode is, and what doesn't need it, with the old "connect
    // on another phone" instruction kept as a quiet line. No primary action, so no pinned bar (§7).
    private void ShowAdminModeNeeded()
    {
        Show(
            ConnectCopy.NeededHeading,
            Paragraph(ConnectCopy.NeededWhy),
            Paragraph(ConnectCopy.NeededWhere),
            Paragraph(ConnectCopy.NeededNotRequired),
            QuietLine(ConnectCopy.Fallback));
        SetBar();
    }

    // The third state: Chrome is not on the phone (or Android is hiding it). Opening Admin mode would
    // not make a browser appear, so this does not send the owner there.
    private void ShowChromeMissing()
    {
        Show(
            ConnectCopy.ChromeMissingHeading,
            Paragraph(ConnectCopy.ChromeMissingWhy),
            QuietLine(ConnectCopy.Fallback));
        SetBar();
    }

    // Frame 2. Three steps and one action; the button is what opens the browser.
    private void ShowAdminModeOpen()
    {
        var blocks = new List<View>();
        for (var i = 0; i < ConnectCopy.OpenSteps.Count; i++)
        {
            blocks.Add(Step(i + 1, ConnectCopy.OpenSteps[i]));
        }

        Show(ConnectCopy.OpenHeading, blocks.ToArray());
        SetBar(openPlaidButton);
    }

    // Frame 3. One teal status sentence while Plaid finishes — updated in place, no spinner and no
    // toast — then the new bank's cards simply appear on Accounts.
    private void ShowFinishingUp()
    {
        var chromeAgain = new Button
        {
            Text = ConnectCopy.OpenChromeAgain,
            Style = PaperResources.Token<Style>("QuietButton"),
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.Start,
        };
        chromeAgain.Clicked += OnOpenChromeAgainClicked;

        var status = new Label
        {
            Text = ConnectCopy.Waiting(updateInstitutionName),
            TextColor = Color.FromArgb(PaperColorTokens.ProgressText),
        };

        Show(ConnectCopy.FinishingHeading, status, Paragraph(ConnectCopy.FinishingBody), chromeAgain);
        checkAgainButton.IsEnabled = !finishing;
        SetBar(checkAgainButton);
    }

    private void Show(string heading, params View[] blocks)
    {
        BodyStack.Children.Clear();
        BodyStack.Children.Add(new Label { Text = heading, Style = PaperResources.Token<Style>("HeadingLabel") });
        foreach (var block in blocks)
        {
            BodyStack.Children.Add(block);
        }

        problemLabel.IsVisible = false;
        BodyStack.Children.Add(problemLabel);
    }

    private void ShowProblem(string sentence)
    {
        problemLabel.Text = sentence;
        problemLabel.IsVisible = true;
    }

    private static Label Paragraph(string text) => new() { Text = text };

    private static Label QuietLine(string text) => new()
    {
        Text = text,
        Style = PaperResources.Token<Style>("FieldLabel"),
        VerticalTextAlignment = TextAlignment.Center,
        MinimumHeightRequest = 48,
    };

    // "1  Chrome opens Plaid's page." — the number in the mono face and the area's ink, the sentence
    // hanging beside it so a wrapped line stays under the words rather than under the digit.
    private static Grid Step(int number, string text)
    {
        var digit = new Label
        {
            Text = number.ToString(),
            FontFamily = "Mono",
            TextColor = LedgerAreas.RuleInk(LedgerArea.Accounts),
        };
        var sentence = new Label { Text = text };
        var row = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(new GridLength(24)), new ColumnDefinition(GridLength.Star)],
            Children = { digit, sentence },
        };
        Grid.SetColumn(digit, 0);
        Grid.SetColumn(sentence, 1);
        return row;
    }

    // ---- actions ----

    private async void OnOpenPlaidClicked(object? sender, EventArgs e)
    {
        openPlaidButton.IsEnabled = false;
        // A key action: answered when the browser has been handed the link; the bank's own answer is the trail.
        var connecting = MauiTapWatch.Action(IndexKeyActions.ConnectBank, TimeSpan.FromMinutes(1));
        trail = PlaidLinkTrail.Start(DateTimeOffset.Now);
        try
        {
            if (await EnsureLinkTokenAsync())
            {
                OpenHostedLink();
                connecting.Succeeded();
            }
            else
            {
                connecting.Failed("no link token");
            }
        }
        catch (Exception ex)
        {
            connecting.Failed($"{ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            openPlaidButton.IsEnabled = true;
        }
    }

    private async void OnOpenChromeAgainClicked(object? sender, EventArgs e)
    {
        // Admin mode can close while the owner is away (it is Flyleaf's to close). Opening a suspended
        // Chrome would only fail, so go back to whichever state is true now; the link token is kept, so
        // the owner is one tap from Plaid again once Admin mode is open.
        if (ReadAdminMode() != AdminModeState.Open)
        {
            opened = false;
            await EvaluateAsync();
            return;
        }

        OpenHostedLink();
    }

    private async void OnCheckAgainClicked(object? sender, EventArgs e) => await TryFinishAsync(auto: false);

    // Creates the link token on demand, the first time the owner asks to go to Plaid — never before, so
    // a phone that never gets that far never mints one. The token is reused if Chrome is opened again.
    private async Task<bool> EnsureLinkTokenAsync()
    {
        if (linkToken is not null && !string.IsNullOrEmpty(hostedLinkUrl))
            return true;

        logger.LogInformation(
            "Connect: creating link token (environment={Environment}, configured={Configured}, updateMode={UpdateMode}).",
            SettingsStore.PlaidEnvironment, SettingsStore.IsConfigured, updateExistingAccessToken is not null);

        try
        {
            var token = updateExistingAccessToken is { } existingAccessToken
                ? await Index.Domain.Connect.ReconnectFlow.BeginAsync(plaid, existingAccessToken, CancellationToken.None)
                : await plaid.CreateLinkTokenAsync(CancellationToken.None);
            logger.LogInformation(
                "Connect: link token created (tokenLength={Length}, hasHostedUrl={HasUrl}).",
                token.LinkToken?.Length ?? 0, !string.IsNullOrEmpty(token.HostedLinkUrl));

            if (string.IsNullOrEmpty(token.HostedLinkUrl))
            {
                logger.LogWarning("Connect: Plaid returned no hosted_link_url; cannot open Link.");
                Trail(t => t.LinkTokenFailed(DateTimeOffset.Now, "no hosted_link_url"));
                ShowProblem("Couldn’t open Plaid Link. Check your credentials and environment in Settings.");
                return false;
            }

            linkToken = token.LinkToken;
            hostedLinkUrl = token.HostedLinkUrl;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Connect: failed to start Plaid Link.");
            Trail(t => t.LinkTokenFailed(DateTimeOffset.Now, ex.GetType().Name));
            ShowProblem($"Couldn’t start Plaid Link: {ex.Message}");
            return false;
        }
    }

    // Sends the hosted link to Chrome by name. The default browser on the locked phone is Flyleaf's
    // restricted viewer, which cannot complete a bank's sign-in, so an unnamed link would land there.
    private void OpenHostedLink()
    {
        if (string.IsNullOrEmpty(hostedLinkUrl))
            return;

        try
        {
            logger.LogInformation("Connect: opening hosted link in Chrome (host={Host}).", HostOf(hostedLinkUrl));
            var host = (Context?)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
                ?? global::Android.App.Application.Context;
            using var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(hostedLinkUrl));
            intent.SetPackage(ConnectBrowserPolicy.ChromePackage);
            intent.AddCategory(Intent.CategoryBrowsable);
            if (host is not global::Android.App.Activity)
                intent.AddFlags(ActivityFlags.NewTask);
            host.StartActivity(intent);

            opened = true;
            ShowFinishingUp();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Connect: failed to open Chrome for Plaid Link.");
            Trail(t => t.ChromeFailed(DateTimeOffset.Now, ex.GetType().Name));
            ShowProblem($"Couldn’t open Chrome: {ex.Message}");
        }
    }

    // Reads the completed Link session and records the institution. Shared by the automatic check when
    // the page reappears after Chrome (auto: true) and the Check again button (auto: false). Polls
    // /link/token/get with backoff so a bank's post-OAuth propagation lag no longer drops the connection.
    // While Plaid has not handed anything over the teal sentence simply stays: nothing new is said.
    private async Task TryFinishAsync(bool auto)
    {
        if (finishing || linkToken is null)
            return;

        if (!opened)
        {
            await EvaluateAsync();
            return;
        }

        finishing = true;
        checkAgainButton.IsEnabled = false;

        try
        {
            logger.LogInformation(
                "Connect: polling for the completed Link session (auto={Auto}, maxAttempts={Attempts}).",
                auto, ConnectPollAttempts);
            // A Check again after a trail has ended begins a fresh one: the earlier attempt is already in the log.
            trail = (trail is { IsFinished: false } open ? open : PlaidLinkTrail.Start(DateTimeOffset.Now).Opened(DateTimeOffset.Now))
                .CameBack(DateTimeOffset.Now);
            var result = await plaid.PollLinkResultAsync(
                linkToken, ConnectPollAttempts, ConnectPollBackoff(), delay: null, CancellationToken.None);
            if (result is null)
            {
                Trail(t => t.NoPublicToken(DateTimeOffset.Now, ConnectPollAttempts));
                logger.LogInformation(
                    "Connect: no completed Link session after {Attempts} attempts; awaiting a retry.",
                    ConnectPollAttempts);
                finishing = false;
                ShowFinishingUp();
                return;
            }

            PlaidItemAccess access;
            try
            {
                access = await plaid.ExchangePublicTokenAsync(result.PublicToken, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Trail(t => t.ExchangeFailed(DateTimeOffset.Now, ex is HttpRequestException { StatusCode: { } status } ? (int)status : 0, ex.GetType().Name));
                throw;
            }

            if (updateInstitutionId is { } institutionId)
            {
                // D150: repair the SAME institution row in place — never AddInstitutionAsync, which
                // would create a second Item (and, on the Trial plan, spend one of the ten it counts).
                await accounts.ReconnectInstitutionAsync(institutionId, access.AccessToken, CancellationToken.None);
                logger.LogInformation("Connect: reconnected institution {InstitutionId} in update mode.", institutionId);
            }
            else
            {
                var name = result.InstitutionName ?? access.InstitutionName ?? "New account";
                var added = await accounts.AddInstitutionAsync(
                    access.ItemId, access.AccessToken, name, CancellationToken.None, result.InstitutionId);
                // The bank's brand colour tints its post-its. Best-effort and never thrown: if Plaid
                // cannot answer now the refresh backfills it, and the connection stands either way.
                await accounts.LookUpInstitutionColorAsync(added.Id, plaid, logger, CancellationToken.None);
                // A single login can carry several cards (e.g. one bank login covers several
                // cards). They arrive as separate accounts under this one Item on the next refresh.
                logger.LogInformation("Connect: connected institution {Institution}; refreshing its accounts.", name);
            }

            Trail(t => t.Connected(DateTimeOffset.Now));

            // Back up the new bank's access token to the encrypted vault so a later reset can restore it
            // without re-linking. The connection already works locally, but surface a failed backup so a
            // silently un-saved token doesn't quietly force a re-link after the next reset.
            SecretsVaultPublishResult backup;
            try { backup = await vaultSync.PublishAsync(CancellationToken.None); }
            catch (Exception ex) { backup = SecretsVaultPublishResult.WriteFailed(ex.Message); }
            if (backup.NeedsAttention)
                await DisplayAlert("Vault backup didn’t complete", backup.Message, "OK");

            // Kick an immediate refresh so balances appear without waiting for the daily cycle, then
            // turn to Accounts, where the new bank's cards appear (no toast: the cards are the news).
            RefreshWorker.RunNow(global::Android.App.Application.Context!);
            await Navigation.PopToRootAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Connect: failed to finish connecting.");
            finishing = false;
            ShowFinishingUp();
            ShowProblem($"Couldn’t finish connecting: {ex.Message}");
        }
    }

    private static string HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "(none)";

    private void Trail(Func<SignInTrail, SignInTrail> step)
    {
        if (trail is null)
        {
            return;
        }
        trail = step(trail);
        if (trail.IsFinished)
        {
            MauiLogLoop.SignIn(trail);
        }
    }
}
