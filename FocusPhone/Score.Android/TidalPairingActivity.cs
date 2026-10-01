using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Codex.Domain.Credentials;
using Codex.Domain.Design;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Ui;
using Codex.Ui.Voice;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;

namespace Score;

/// <summary>
/// TIDAL sign-in: the browser flow (authorization code + PKCE).
///
/// This was the device-code flow (RFC 8628) — a short code typed on a laptop, no
/// browser on the phone — which is by far the better shape for a locked-down device.
/// TIDAL refuses it: "Client is not a Limited Input Device client", and their own
/// Android SDK documents that flow as available to TIDAL's internal apps only. So the
/// phone opens a browser after all, and the lockdown is what makes that acceptable:
/// the launcher un-suspends the system browser for OAuth Custom Tabs only, against an
/// allowlist of identity hosts, with everything else blocked. The same door the
/// GitHub sign-in goes through.
///
/// The security that the device flow got from having no redirect at all, this gets
/// from PKCE: the code comes back through a URL any app could in principle receive,
/// and is worthless without the verifier, which never leaves. The state is checked
/// before the code is exchanged, so a code from somewhere else cannot be spent here.
///
/// The result is a refresh token, saved to <see cref="TidalAccountStore"/> —
/// <see cref="PlaybackService"/> reads it from there on its next launch to build the
/// TIDAL session (see that class's remarks). Nothing here talks to
/// <see cref="PlaybackService"/> directly; a fresh sign-in takes effect on the next
/// cold start, the same way most of this app's vault-backed settings do.
///
/// One status sentence carries the whole flow (§6: no spinners).
///
/// SingleTask, and the intent filter, are what bring the redirect back into THIS
/// screen rather than a second copy of it with none of the flow's state in it — the
/// verifier and the state live in this instance and nowhere else.
/// </summary>
[Activity(
    Label = "TIDAL",
    Theme = "@style/Theme.FocusPhone.Paper",
    LaunchMode = Android.Content.PM.LaunchMode.SingleTask,
    Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "com.storyworthtelling.focusphone.audio",
    DataHost = "tidal-auth")]
public class TidalPairingActivity : Activity
{
    private readonly ICredentialHealthSink health = new CredentialHealthStore();

    private LinearLayout body = null!;
    private string status = string.Empty;
    private bool signedIn;
    private CancellationTokenSource? pairing;

    // The half of the sign-in that must never travel. Held for the round trip out to
    // the browser and back, and cleared the moment it is spent or abandoned.
    private PkceCodes? codes;
    private string? expectedState;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        signedIn = new TidalAccountStore(this).Load() is not null;
        SetContentView(BuildLayout());
        Render();
    }

    // The browser hands the redirect back here rather than starting a new screen,
    // because SingleTask keeps the instance holding the verifier.
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        if (intent?.Data is { } data)
        {
            _ = FinishSignInAsync(data.ToString());
        }
    }

    protected override void OnDestroy()
    {
        pairing?.Cancel();
        base.OnDestroy();
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var scroll = new ScrollView(this);
        body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        body.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X4), 0, 0);
        scroll.AddView(body);

        root.AddView(BinderFrame.Wrap(
            this, "TIDAL", ScoreShellAreas.All[ScoreShellAreas.Settings].Hue, scroll,
            ScoreShellAreas.All, ScoreShellAreas.Settings, SwitchArea),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        root.AddView(PaperUi.BottomBar(this, PaperUi.QuietButton(this, "Close", Finish)));
        return root;
    }

    // Names the tab ScoreActivity should land on once this screen finishes (see
    // ScoreActivity.PendingArea); ReorderToFront brings its existing instance forward rather
    // than creating a second one, or starts it fresh if it isn't running at all.
    private void SwitchArea(int index)
    {
        ScoreActivity.PendingArea = index;
        var intent = new Intent(this, typeof(ScoreActivity));
        intent.AddFlags(ActivityFlags.ReorderToFront);
        StartActivity(intent);
        Finish();
    }

    private void Render()
    {
        body.RemoveAllViews();

        if (signedIn)
        {
            body.AddView(PaperUi.Body(this, "Signed in to TIDAL."));
            body.AddView(PaperUi.SecondaryButton(this, "Sign out", SignOut));
            return;
        }

        if (!string.IsNullOrEmpty(status))
        {
            body.AddView(PaperUi.Secondary(this, status));
        }

        body.AddView(PaperUi.Secondary(this,
            "Signing in opens TIDAL's own page in a browser. It is the only sign-in TIDAL " +
            "offers an app like this one."));
        body.AddView(PaperUi.PrimaryButton(this, "Sign in to TIDAL", () => _ = StartSignInAsync()));
    }

    private void SignOut()
    {
        // Reaches the vault too, or the next launch restores the account that was
        // just signed out of.
        _ = new TidalAccountSync(this).SignOutAsync(CancellationToken.None);
        signedIn = false;
        status = string.Empty;
        Forget();
        Render();
    }

    private async Task StartSignInAsync()
    {
        status = "Reading TIDAL app credentials…"; // §6: loading is a sentence
        Render();

        var credentials = await ReadCredentialsAsync();
        if (credentials is not ({ Length: > 0 } clientId, var clientSecret))
        {
            health.MarkFailing(CredentialIds.Tidal, "Vault has no TIDAL client id/secret.");
            status = "TIDAL app credentials aren't set up yet.";
            Render();
            return;
        }

        // Generated per sign-in and held only in this instance. Anything already in
        // flight is abandoned rather than left to race the new one back.
        Forget();
        codes = PkceCodes.Create();
        expectedState = TidalSignIn.NewState();

        var url = TidalSignIn.AuthorizeUrl(clientId, TidalSignIn.RedirectUri, codes.Challenge, expectedState);

        try
        {
            OpenInBrowser(url);

            // A refusal on TIDAL's page never comes back here, so this is the only record of
            // what was asked for — the three things the portal must agree with. Nothing
            // secret: the client id is in every authorize URL, and the PKCE pair is not logged.
            AudioDiagnostics.LogWarning(
                "TidalPairingActivity",
                $"Opened TIDAL sign-in: client {clientId}, redirect {TidalSignIn.RedirectUri}, scope \"{TidalSignIn.Scope}\".");
            status = TidalSignInFailure.WhileAtTidal;
        }
        catch (ActivityNotFoundException e)
        {
            // On the locked phone the browser is suspended except for this; if it is
            // gone entirely there is nothing to fall back to, and saying so beats a
            // screen that looks like it is still waiting.
            AudioDiagnostics.LogWarning("TidalPairingActivity", "No browser to sign in with.", e);
            Forget();
            status = "There's no browser on this phone to sign in with.";
        }

        // The client secret is not held across the round trip — it is re-read from the
        // vault when the redirect comes back, so it is in memory for two short windows
        // rather than for however long the owner spends typing a password.
        _ = clientSecret;
        Render();
    }

    private async Task FinishSignInAsync(string redirect)
    {
        if (codes is not { } pkce || expectedState is not { } state)
        {
            // A redirect with no sign-in waiting for it — a stale link, or a second
            // one after the first was spent. Nothing to do, and nothing to alarm about.
            return;
        }

        var result = OAuthRedirect.Read(redirect, state);
        if (result.Status != OAuthRedirectStatus.Authorized || result.Code is not { } code)
        {
            if (result.Status != OAuthRedirectStatus.Declined)
            {
                AudioDiagnostics.LogWarning(
                    "TidalPairingActivity", $"TIDAL sign-in came back unusable: {result.Error}");
            }

            Forget();
            status = result.Status switch
            {
                OAuthRedirectStatus.Declined => "Sign-in was cancelled.",
                OAuthRedirectStatus.StateMismatch => "That sign-in didn't match the one this screen started.",
                _ => "Something went wrong signing in to TIDAL. Try again.",
            };
            Render();
            return;
        }

        status = "Finishing sign-in…";
        Render();

        var credentials = await ReadCredentialsAsync();
        if (credentials is not ({ Length: > 0 } clientId, var clientSecret))
        {
            health.MarkFailing(CredentialIds.Tidal, "Vault has no TIDAL client id/secret.");
            Forget();
            status = "TIDAL app credentials aren't set up yet.";
            Render();
            return;
        }

        TidalTokenPollResult exchanged;
        try
        {
            using var http = new HttpClient();
            exchanged = await new TidalDeviceAuthClient(http).ExchangeCodeAsync(
                clientId, clientSecret, code, pkce.Verifier, TidalSignIn.RedirectUri, CancellationToken.None);
        }
        catch (Exception e)
        {
            AudioDiagnostics.LogWarning("TidalPairingActivity", "Exchanging the TIDAL code failed.", e);
            Forget();
            status = TidalSignInFailure.Describe(e.Message);
            if (TidalSignInFailure.IsCredentialRejection(e.Message))
            {
                health.MarkFailing(CredentialIds.Tidal, status);
            }

            Render();
            return;
        }

        // Spent either way: a code is single-use, and a verifier that outlives its
        // exchange is a secret kept for no reason.
        Forget();

        if (exchanged.Status == TidalTokenPollStatus.Success && !string.IsNullOrEmpty(exchanged.RefreshToken))
        {
            health.MarkOk(CredentialIds.Tidal);
            var account = new TidalAccount(exchanged.UserId ?? "", exchanged.RefreshToken);
            new TidalAccountStore(this).Save(account);

            // Mirrored into this person's own vault so a factory reset costs a
            // passphrase rather than another sign-in. Best-effort, off the UI thread.
            _ = new TidalAccountSync(this).BackUpAsync(account, CancellationToken.None);
            signedIn = true;
            status = string.Empty;
            Render();
            return;
        }

        AudioDiagnostics.LogWarning(
            "TidalPairingActivity", $"TIDAL refused the code exchange: {exchanged.Status}." +
                (string.IsNullOrEmpty(exchanged.Detail) ? "" : $" {exchanged.Detail}"));
        status = string.IsNullOrEmpty(exchanged.Detail)
            ? "TIDAL wouldn't complete the sign-in. Try again."
            : $"TIDAL wouldn't complete the sign-in: {exchanged.Detail}";
        if (TidalSignInFailure.IsCredentialRejection(exchanged.Detail))
        {
            health.MarkFailing(CredentialIds.Tidal, status);
        }
        Render();
    }

    private async Task<(string ClientId, string ClientSecret)?> ReadCredentialsAsync()
    {
        var vaultAccess = new SecretsVaultAccess(
            new Score.Google.DeviceAccountTokenProvider().GetAccessToken, "Score",
            () => ScoreIdentity.Account);
        var vaultResult = await vaultAccess.Service.LoadAsync(CancellationToken.None);

        return vaultResult.Status == SecretsVaultStatus.Loaded
            ? (vaultResult.Vault.Tidal.ClientId, vaultResult.Vault.Tidal.ClientSecret)
            : null;
    }

    /// <summary>
    /// A Custom Tab rather than a browser window: it closes itself on the redirect and
    /// never becomes a place to browse from. The launcher's policy un-suspends the
    /// system browser for exactly this and blocks every URL outside the identity
    /// allowlist, so the tab can reach TIDAL's login and nothing else.
    /// </summary>
    private void OpenInBrowser(string url)
    {
        var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));

        // The Custom Tabs handshake, without taking the AndroidX Browser dependency for
        // one extra: these are the extras a supporting browser reads, and one that does
        // not simply opens the page as an ordinary view.
        intent.PutExtra("android.support.customtabs.extra.SESSION", (Bundle?)null);
        intent.PutExtra("android.support.customtabs.extra.TOOLBAR_COLOR", PaperTheme.Paper.ToArgb());
        intent.AddFlags(ActivityFlags.NoHistory);
        StartActivity(intent);
    }

    // Drops the flow's secrets. Called when a sign-in is spent, abandoned, or replaced.
    private void Forget()
    {
        pairing?.Cancel();
        pairing = null;
        codes = null;
        expectedState = null;
    }
}
