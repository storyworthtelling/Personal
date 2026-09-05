using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Codex.Domain.Design;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Ui;
using Codex.Ui.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Score;

/// <summary>
/// TIDAL's device-code sign-in (RFC 8628): the screen shows a short code and a link,
/// the owner approves it on another device's browser, and this polls until that
/// approval lands — the same shape a smart-TV app's "go to tv.example.com/pair"
/// screen takes, chosen for exactly the reason those exist: a locked-down phone
/// browser is a worse place to type a TIDAL password than a laptop already signed in.
///
/// The result is a refresh token, saved to <see cref="TidalAccountStore"/> —
/// <see cref="PlaybackService"/> reads it from there on its next launch to build the
/// TIDAL session (see that class's remarks). Nothing here talks to
/// <see cref="PlaybackService"/> directly; a fresh sign-in takes effect on the next
/// cold start, the same way most of this app's vault-backed settings do.
///
/// One status sentence carries the whole flow (§6: no spinners), and the code itself
/// is the one thing on screen worth a monospace face — it is meant to be typed
/// somewhere else character-for-character.
/// </summary>
[Activity(Label = "TIDAL", Theme = "@style/Theme.FocusPhone.Paper")]
public class TidalPairingActivity : Activity
{
    private LinearLayout body = null!;
    private string status = string.Empty;
    private string? code;
    private string? verificationUri;
    private bool signedIn;
    private CancellationTokenSource? pairing;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        signedIn = new TidalAccountStore(this).Load() is not null;
        SetContentView(BuildLayout());
        Render();
    }

    protected override void OnDestroy()
    {
        pairing?.Cancel();
        base.OnDestroy();
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        var header = PaperUi.PageHeader(this, "TIDAL");
        header.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        root.AddView(header);

        var scroll = new ScrollView(this);
        body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        body.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X4), edge, 0);
        scroll.AddView(body);
        root.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        root.AddView(PaperUi.BottomBar(this, PaperUi.QuietButton(this, "Close", Finish)));
        return root;
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

        if (code is { Length: > 0 })
        {
            body.AddView(PaperUi.Mono(this, code));
            if (verificationUri is { Length: > 0 })
            {
                body.AddView(PaperUi.SecondaryButton(this, verificationUri,
                    () => StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(verificationUri)))));
            }
        }
        else
        {
            body.AddView(PaperUi.PrimaryButton(this, "Get a code", () => _ = StartPairingAsync()));
        }
    }

    private void SignOut()
    {
        // Reaches the vault too, or the next launch restores the account that was
        // just signed out of.
        _ = new TidalAccountSync(this).SignOutAsync(CancellationToken.None);
        signedIn = false;
        status = string.Empty;
        code = null;
        verificationUri = null;
        Render();
    }

    private async Task StartPairingAsync()
    {
        status = "Reading TIDAL app credentials…"; // §6: loading is a sentence
        Render();

        var vaultAccess = new SecretsVaultAccess(
            new Score.Google.DeviceAccountTokenProvider().GetAccessToken, "Score",
            () => ScoreIdentity.Account);
        var vaultResult = await vaultAccess.Service.LoadAsync(CancellationToken.None);
        if (vaultResult.Status != SecretsVaultStatus.Loaded || string.IsNullOrEmpty(vaultResult.Vault.Tidal.ClientId))
        {
            status = "TIDAL app credentials aren't set up yet.";
            Render();
            return;
        }

        var clientId = vaultResult.Vault.Tidal.ClientId;
        var clientSecret = vaultResult.Vault.Tidal.ClientSecret;
        var client = new TidalDeviceAuthClient(new HttpClient());

        status = "Getting a code…";
        Render();

        TidalDeviceAuthorization authorization;
        try
        {
            authorization = await client.RequestDeviceCodeAsync(clientId, clientSecret, CancellationToken.None);
        }
        catch (Exception e)
        {
            AudioDiagnostics.LogWarning(
                "TidalPairingActivity", "Requesting a TIDAL device code failed.", e);

            // The trace goes to Diagnostics; the screen gets the one sentence that says
            // what to do about it. Offering a retry for something a retry cannot fix
            // sends the owner round a loop instead of to the screen that settles it.
            status = TidalSignInFailure.Describe(e.Message);
            Render();
            return;
        }

        code = authorization.UserCode;
        verificationUri = authorization.VerificationUriComplete;
        status = "Enter this code, then come back here.";
        Render();

        pairing?.Cancel();
        pairing = new CancellationTokenSource();
        var result = await client.PollUntilAuthorizedAsync(
            clientId, clientSecret, authorization.DeviceCode,
            authorization.Interval, authorization.ExpiresIn,
            (delay, ct) => Task.Delay(delay, ct), pairing.Token);

        if (result.Status == TidalTokenPollStatus.Success && !string.IsNullOrEmpty(result.RefreshToken))
        {
            var account = new TidalAccount(result.UserId ?? "", result.RefreshToken);
            new TidalAccountStore(this).Save(account);

            // Mirrored into this person's own vault so a factory reset costs a
            // passphrase rather than another pairing. Best-effort, off the UI thread.
            _ = new TidalAccountSync(this).BackUpAsync(account, CancellationToken.None);
            signedIn = true;
            code = null;
            verificationUri = null;
            status = string.Empty;
            Render();
            return;
        }

        code = null;
        verificationUri = null;
        status = result.Status switch
        {
            TidalTokenPollStatus.Denied => "That code was declined. Get a new one to try again.",
            TidalTokenPollStatus.Expired => "That code expired before it was approved. Get a new one.",
            _ => "Something went wrong signing in to TIDAL. Try again.",
        };
        Render();
    }
}
