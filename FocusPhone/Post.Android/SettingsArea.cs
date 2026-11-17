using Storyworthtelling.Diagnostics;
using Codex.Domain.Credentials;
using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Post;
using Codex.Ui;
using Codex.Ui.Voice;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;
using Storyworthtelling.Updates;
using Storyworthtelling.Materials;

namespace Post;

/// <summary>
/// Settings: the mail account, the vault passphrase, sign-in, and the way through to
/// Diagnostics. Every app in the suite has this area and it is the one that looks the
/// same in all of them, which is why its tab is the neutral gray rather than an identity
/// or role hue (§9).
///
/// A working credential is a done state, not something to keep on screen: a key that has
/// not been rejected collapses to a status line with a Replace button, because the vault
/// backs it up and restores it anyway (<see cref="CredentialFieldState"/>).
/// </summary>
public sealed class SettingsArea(Activity host, VoiceGoogleAccount voiceAccount) : PostArea(host)
{
    private readonly AccountStore account = new(host);

    private EditText? address;
    private EditText? password;
    private EditText? passphrase;

    public override BinderArea Tab =>
        new("Settings", TabGlyphs.Settings, PaperTheme.ResolvedMark, PaperTheme.ResolvedFill);

    public override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == VoiceGoogleAccount.RequestCode)
        {
            VoiceGoogleAccount.HandleChooserResult(resultCode, data);
        }
    }

    private SettingsAccordion? accordion;

    private LinearLayout Column() => new(Host) { Orientation = Orientation.Vertical };

    public override View BuildView()
    {
        var scroll = new ScrollView(Host);
        var layout = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        // The suite's anatomy (audit W8.4b): Connections, Vault, Post's own, Diagnostics, then the build line.
        var connections = Column();
        var vault = Column();
        var messages = Column();
        var diagnosticsBody = Column();

        address = PaperUi.Field(Host, string.Empty);
        address.Text = account.GmailAddress;
        PaperUi.CommitsAsTyped(address, text => { account.GmailAddress = text; changed = true; }, Settle);
        AddFieldGroup(connections, "Gmail address", address, null);

        password = PaperUi.MonoField(Host, string.Empty);
        password.Text = account.GmailAppPassword;
        password.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;
        password.Typeface = PaperTheme.Mono(Host); // setting InputType resets the typeface
        // A blank never overwrites the stored password.
        PaperUi.CommitsAsTyped(password, text =>
        {
            if (text.Trim().Length > 0)
            {
                account.GmailAppPassword = text.Trim();
                changed = true;
            }
        }, Settle);

        // The field object is always created, and stores itself as it is typed, even while it is hidden.
        const string passwordHelper = "Make one in your Google Account, then paste it here.";
        var passwordContainer = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        connections.AddView(passwordContainer, Spaced(0, 0));
        void ShowPasswordField(string helper)
        {
            passwordContainer.RemoveAllViews();
            AddFieldGroup(passwordContainer, "App password", password, helper);
            KeyConsoleLink.AddTo(passwordContainer, CredentialIds.GmailAppPassword);
        }
        // A working app password is not on the screen at all (owner's ruling, audit W8.4b): it appears only when
        // it is missing or Gmail just refused it.
        var passwordState = CredentialFieldState.Decide(
            !string.IsNullOrEmpty(account.GmailAppPassword),
            HeadCredentialHealth.Store().Load().Get(CredentialIds.GmailAppPassword));
        if (passwordState.Appears)
        {
            ShowPasswordField(passwordState.IsWarning ? passwordState.StatusText : passwordHelper);
        }

        var enableMessages = PaperUi.SecondaryButton(Host, "Let Post read message notifications", () =>
            Host.StartActivity(new Intent(Android.Provider.Settings.ActionNotificationListenerSettings)));
        messages.AddView(enableMessages, Spaced(0, 0));

        var note = PaperUi.Secondary(Host,
            "Post reads incoming texts from Google Messages notifications and " +
            "voicemails, with their transcripts, from Google Phone notifications. Call and text " +
            "history need the call log and SMS permissions, which Flyleaf grants once the " +
            "phone is locked down.");
        messages.AddView(note, Spaced(DesignTokens.Space.X4, 0));

        // On a locked-down phone there is no logcat, so surface captured crashes and
        // background failures here where the owner can review and share them.
        var diagnostics = PaperUi.QuietButton(Host, "Diagnostics", () =>
            Host.StartActivity(new Intent(Host, typeof(DiagnosticsActivity))));
        diagnosticsBody.AddView(diagnostics, Spaced(0, 0));

        // Which path mail is actually taking. Worth stating plainly, because the two behave
        // differently in a way the owner will notice: only the API can really trash a
        // message or have Gmail run the search.
        var route = MailRoute.Choose(VoiceGoogleAccount.IsSignedIn, account.IsConfigured);
        connections.AddView(PaperUi.Secondary(Host, route switch
        {
            MailTransport.GmailApi => "Mail comes through your Google sign-in. Deleting moves mail to Gmail’s trash, and Gmail runs the search.",
            MailTransport.AppPassword => "Mail comes through the app password. Sign in with Google below so deleting really trashes and Gmail runs the search.",
            _ => "No mail account yet.",
        }), Spaced(DesignTokens.Space.X6, 0));

        // The Gmail API's own refusal is recorded under gmail-account, which this screen never read (audit 11 §12).
        var gmailRefused = HeadCredentialHealth.Store().Load().Get(CredentialIds.GmailAccount).Status == CredentialStatus.Failing;
        var voiceStatus = PaperUi.Secondary(Host, VoiceGoogleAccount.IsSignedIn
            ? GoogleSignInHealth.NeedsSignIn()
                ? $"Google turned the sign-in for {VoiceGoogleAccount.Email} away. Sign in again."
                : gmailRefused
                    ? $"Gmail refused {VoiceGoogleAccount.Email}, so mail is waiting. Sign in again to grant mail access."
                    : $"Signed in as {VoiceGoogleAccount.Email}."
            : "Sign in with Google so mail comes through your account, and so your keys back up to the vault.");
        var voiceSignIn = PaperUi.SecondaryButton(Host, gmailRefused ? GoogleSignInHealth.SignInAgain : GoogleSignInHealth.ButtonLabel("Sign in with Google"), () => _ = SignInAsync(voiceStatus));
        connections.AddView(voiceSignIn, Spaced(DesignTokens.Space.X6, 0));
        connections.AddView(voiceStatus, Spaced(DesignTokens.Space.X2, 0));

        // Texts: Post works through Google Messages over the Messages-for-Web bridge, so the
        // one setup step is pairing it. The line says where texting stands; the button pairs
        // (or pairs again after Messages unpaired it).
        var bridge = GmBridgeClient.Shared(Host);
        connections.AddView(PaperUi.Secondary(Host, TextsSetting.Describe(bridge.IsPaired, bridge.IsConnected)),
            Spaced(DesignTokens.Space.X6, 0));
        connections.AddView(PaperUi.SecondaryButton(Host,
            bridge.IsPaired ? "Connect to Google Messages again" : "Connect to Google Messages",
            () => Host.StartActivity(new Intent(Host, typeof(MessagesPairingActivity)))),
            Spaced(DesignTokens.Space.X2, 0));

        // The vault passphrase is the one secret NOT kept in the vault, so it is shown in
        // the clear rather than masked: it has to be read back and set identically in
        // every app, and Android blocks clipboard copy on a password-typed field.
        passphrase = PaperUi.MonoField(Host, "Choose a passphrase");
        passphrase.Text = VaultPassphraseStore.Passphrase;
        PaperUi.CommitsAsTyped(passphrase, text => { VaultPassphraseStore.Passphrase = text; changed = true; }, Settle);
        AddFieldGroup(vault, "Vault passphrase", passphrase,
            "Backs up your Gmail credentials to your Google Drive, encrypted. Restores after a reset. " +
            "Shown in the clear so you can copy it into your other apps.");

        accordion ??= new SettingsAccordion(Host, Invalidate);
        accordion.Draw(layout,
        [
            new SettingsSection(SettingsAnatomy.Connections, null, body => body.AddView(connections)),
            new SettingsSection(SettingsAnatomy.Vault, null, body => body.AddView(vault)),
            new SettingsSection("Messages", null, body => body.AddView(messages)),
            new SettingsSection(SettingsAnatomy.Diagnostics, null, body => body.AddView(diagnosticsBody)),
        ]);

        // The quiet foot line: which build is actually on the phone. The channel installs
        // silently and most updates change nothing visible, so without it "did that land?"
        // has no answer on the device.
        var buildLine = PaperUi.BuildNumber(Host, "Post");
        var buildParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        buildParams.TopMargin = PaperTheme.Dp(Host, DesignTokens.Space.X8);
        layout.AddView(buildLine, buildParams);

        scroll.AddView(layout);
        return scroll;
    }

    // There is no Save (§5): each field stores itself as it is typed, and what follows a change — the
    // vault backup and a sync — runs when a field loses focus or the screen goes away.
    public override void Pause() => Settle();

    private bool changed;

    private void Settle()
    {
        if (!changed)
        {
            return;
        }

        changed = false;

        // Back up the credentials to the encrypted Drive vault. Best-effort — they already
        // work locally, so no passphrase/Google/network yet just means the vault picks
        // them up on the next change.
        PublishGmailToVault();

        if (account.IsConfigured)
        {
            // Sync now so correspondence appears promptly instead of waiting for the next
            // periodic run.
            EmailSyncWorker.RunOnce(Host);
        }
    }

    // Opens the system Google account picker and requests Drive consent, so the vault can
    // be read and written. The picker result returns through OnActivityResult.
    //
    // The sign-in is trailed (LOG-LOOP-PLAN.md §4.2): every way it can end is recorded with a reason
    // sentence, including a picker or consent screen that never answers, which Resume checks for.
    private async Task SignInAsync(TextView status)
    {
        var signingIn = TapWatch.Action(PostKeyActions.GmailSignIn, TimeSpan.FromMinutes(10));
        try
        {
            var email = await voiceAccount.SignIn(Host, SignInFlowKinds.GmailApi, PostDiagnostics.LogSignIn);
            status.Text = $"Signed in as {email}.";
            signingIn.Succeeded();
        }
        catch (Exception ex)
        {
            status.Text = $"Sign-in didn’t complete. {Storyworthtelling.Diagnostics.PlainError.Describe(ex, "Google")}";
            signingIn.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public override void Resume() => VoiceGoogleAccount.CheckPendingSignIn(DateTimeOffset.Now, resumed: true);

    private async void PublishGmailToVault()
    {
        try
        {
            var access = new SecretsVaultAccess(voiceAccount.GetAccessToken, "Post");
            await access.Service.PublishAsync(v => v with
            {
                Gmail = new GmailSecrets(
                    account.GmailAddress ?? string.Empty,
                    account.GmailAppPassword ?? string.Empty,
                    account.GmBridgeSession ?? string.Empty)
            }, CancellationToken.None);
        }
        catch
        {
            // Best-effort: the vault picks the credentials up on the next successful save.
        }
    }

    private void AddFieldGroup(LinearLayout layout, string label, EditText field, string? helper)
    {
        var labelView = PaperUi.Secondary(Host, label);
        labelView.TextSize = DesignTokens.TypeSp.Label;
        labelView.Typeface = PaperTheme.SansSemibold(Host);
        layout.AddView(labelView, Spaced(0, DesignTokens.Space.X1));
        layout.AddView(field, Spaced(0, helper is null ? DesignTokens.Space.X4 : DesignTokens.Space.X1));
        if (helper is not null)
        {
            layout.AddView(PaperUi.Secondary(Host, helper), Spaced(0, DesignTokens.Space.X4));
        }
    }

    private LinearLayout.LayoutParams Spaced(int topDp, int bottomDp) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(Host, topDp),
            BottomMargin = PaperTheme.Dp(Host, bottomDp),
        };
}
