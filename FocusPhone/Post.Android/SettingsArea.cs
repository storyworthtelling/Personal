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
        new("Settings", Resource.Drawable.area_settings, PaperTheme.ResolvedMark, PaperTheme.ResolvedFill);

    public override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == VoiceGoogleAccount.RequestCode)
        {
            VoiceGoogleAccount.HandleChooserResult(resultCode, data);
        }
    }

    public override View BuildView()
    {
        var scroll = new ScrollView(Host);
        var layout = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        address = PaperUi.Field(Host, "name@gmail.com");
        address.Text = account.GmailAddress;
        AddFieldGroup(layout, "Gmail address", address, null);

        password = PaperUi.MonoField(Host, string.Empty);
        password.Text = account.GmailAppPassword;
        password.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;
        password.Typeface = PaperTheme.Mono(Host); // setting InputType resets the typeface

        // The field object is always created so Save reads its pre-filled value back even
        // while the field itself is hidden.
        const string passwordHelper = "Create one at myaccount.google.com/apppasswords.";
        var passwordContainer = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        layout.AddView(passwordContainer, Spaced(0, 0));
        void ShowPasswordField(string helper)
        {
            passwordContainer.RemoveAllViews();
            AddFieldGroup(passwordContainer, "App password", password, helper);
        }
        void ShowPasswordStatus(string statusText)
        {
            passwordContainer.RemoveAllViews();
            AddCredentialStatus(passwordContainer, "App password", statusText, () =>
            {
                password.Text = string.Empty;
                ShowPasswordField(passwordHelper);
                password.RequestFocus();
            });
        }
        var passwordState = CredentialFieldState.Decide(
            !string.IsNullOrEmpty(account.GmailAppPassword),
            new CredentialHealthStore().Load().Get(CredentialIds.GmailAppPassword));
        if (passwordState.ShowEntry)
        {
            ShowPasswordField(passwordState.IsWarning ? passwordState.StatusText : passwordHelper);
        }
        else
        {
            ShowPasswordStatus(passwordState.StatusText);
        }

        var enableMessages = PaperUi.SecondaryButton(Host, "Enable message sync (notification access)", () =>
            Host.StartActivity(new Intent(Android.Provider.Settings.ActionNotificationListenerSettings)));
        layout.AddView(enableMessages, Spaced(DesignTokens.Space.X4, 0));

        var note = PaperUi.Secondary(Host,
            "Message sync reads incoming texts from Google Messages notifications and " +
            "voicemails (with transcription) from Google Phone notifications. Call and text " +
            "history require the call-log and SMS permissions, which take effect after the " +
            "device is locked down (or a whitelisted install).");
        layout.AddView(note, Spaced(DesignTokens.Space.X6, 0));

        // On a locked-down phone there is no logcat, so surface captured crashes and
        // background failures here where the owner can review and share them.
        var diagnostics = PaperUi.QuietButton(Host, "Diagnostics", () =>
            Host.StartActivity(new Intent(Host, typeof(DiagnosticsActivity))));
        layout.AddView(diagnostics, Spaced(DesignTokens.Space.X4, 0));

        // Which path mail is actually taking. Worth stating plainly, because the two behave
        // differently in a way the owner will notice: only the API can really trash a
        // message or have Gmail run the search.
        var route = MailRoute.Choose(VoiceGoogleAccount.IsSignedIn, account.IsConfigured);
        layout.AddView(PaperUi.Secondary(Host, route switch
        {
            MailTransport.GmailApi => "Mail is going through the Gmail API. Deleting really trashes, and search runs on Gmail.",
            MailTransport.AppPassword => "Mail is going through IMAP on the app password. Sign in with Google below to move to the Gmail API.",
            _ => "No mail account yet.",
        }), Spaced(DesignTokens.Space.X6, 0));

        var voiceStatus = PaperUi.Secondary(Host, VoiceGoogleAccount.IsSignedIn
            ? $"Signed in as {VoiceGoogleAccount.Email}."
            : "Sign in with Google to move mail to the Gmail API and back up your credentials to your encrypted vault.");
        var voiceSignIn = PaperUi.SecondaryButton(Host, "Sign in with Google", () => _ = SignInAsync(voiceStatus));
        layout.AddView(voiceSignIn, Spaced(DesignTokens.Space.X6, 0));
        layout.AddView(voiceStatus, Spaced(DesignTokens.Space.X2, 0));

        // The vault passphrase is the one secret NOT kept in the vault, so it is shown in
        // the clear rather than masked: it has to be read back and set identically in
        // every app, and Android blocks clipboard copy on a password-typed field.
        passphrase = PaperUi.MonoField(Host, "Choose a passphrase");
        passphrase.Text = VaultPassphraseStore.Passphrase;
        AddFieldGroup(layout, "Secrets vault passphrase", passphrase,
            "Backs up your Gmail credentials to your Google Drive, encrypted. Restores after a reset. " +
            "Shown in the clear so you can copy it into your other apps.");

        scroll.AddView(layout);
        return scroll;
    }

    // Saving is the area's one primary action, so it lives in the pinned bottom bar (§7).
    // Success is the state change — no toast, no dialog.
    public override View? BuildBottomBar() =>
        PaperUi.BottomBar(Host, PaperUi.PrimaryButton(Host, "Save", Save));

    private void Save()
    {
        account.GmailAddress = address?.Text;
        account.GmailAppPassword = password?.Text;
        VaultPassphraseStore.Passphrase = passphrase?.Text ?? string.Empty;

        // Back up the credentials to the encrypted Drive vault. Best-effort — they already
        // work locally, so no passphrase/Google/network yet just means the vault picks
        // them up on the next save.
        PublishGmailToVault();

        if (account.IsConfigured)
        {
            // Sync now so correspondence appears promptly instead of waiting for the next
            // periodic run.
            EmailSyncWorker.RunOnce(Host);
        }
        Invalidate();
    }

    // Opens the system Google account picker and requests Drive consent, so the vault can
    // be read and written. The picker result returns through OnActivityResult.
    private async Task SignInAsync(TextView status)
    {
        try
        {
            var email = await voiceAccount.SignIn(Host);
            status.Text = $"Signed in as {email}.";
        }
        catch (Exception ex)
        {
            status.Text = $"Sign-in didn't complete. {ex.Message}";
        }
    }

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
        labelView.SetTextColor(PaperTheme.Ink);
        layout.AddView(labelView, Spaced(0, DesignTokens.Space.X1));
        layout.AddView(field, Spaced(0, helper is null ? DesignTokens.Space.X4 : DesignTokens.Space.X1));
        if (helper is not null)
        {
            layout.AddView(PaperUi.Secondary(Host, helper), Spaced(0, DesignTokens.Space.X4));
        }
    }

    // The collapsed form of a healthy credential: the same field label, then a status line
    // ("Working ✓") with a Replace button that swaps the real field back in for a
    // deliberate rotation.
    private void AddCredentialStatus(LinearLayout layout, string label, string statusText, Action onReplace)
    {
        var labelView = PaperUi.Secondary(Host, label);
        labelView.TextSize = DesignTokens.TypeSp.Label;
        labelView.Typeface = PaperTheme.SansSemibold(Host);
        labelView.SetTextColor(PaperTheme.Ink);
        layout.AddView(labelView, Spaced(0, DesignTokens.Space.X1));

        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(PaperUi.Secondary(Host, statusText),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(PaperUi.QuietButton(Host, "Replace", onReplace));
        layout.AddView(row, Spaced(0, DesignTokens.Space.X4));
    }

    private LinearLayout.LayoutParams Spaced(int topDp, int bottomDp) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(Host, topDp),
            BottomMargin = PaperTheme.Dp(Host, bottomDp),
        };
}
