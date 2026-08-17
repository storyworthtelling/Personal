using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Codex.Ui.Voice;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;

namespace FocusPhone.Communication;

/// <summary>
/// Gmail account setup: address + app password. Reached by long-pressing the
/// thread list; on a locked-down phone this is where admin mode rotates the
/// app password.
/// </summary>
[Activity(
    Label = "Settings",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class SettingsActivity : Activity
{
    private readonly Codex.Ui.Voice.VoiceGoogleAccount voiceAccount = new();

    // Opens the system Google account picker and requests read-only Drive consent, so the inbox can read
    // the shared owner's-voice profile. The picker result returns through OnActivityResult.
    private async Task VoiceSignInAsync(TextView status)
    {
        try
        {
            var email = await voiceAccount.SignIn(this);
            status.Text = $"Voiced greeting on, signed in as {email}.";
        }
        catch (Exception ex)
        {
            status.Text = $"Sign-in didn't complete. {ex.Message}";
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == Codex.Ui.Voice.VoiceGoogleAccount.RequestCode)
            Codex.Ui.Voice.VoiceGoogleAccount.HandleChooserResult(resultCode, data);
    }

    // Publishes the Gmail address, app password, and Messages-for-Web session to this owner's encrypted
    // secrets vault so a later reset can restore them. Best-effort and off the UI thread.
    private async void PublishGmailToVault(AccountStore account)
    {
        try
        {
            var access = new SecretsVaultAccess(voiceAccount.GetAccessToken, "FocusPhone.Communication");
            await access.Service.PublishAsync(v => v with
            {
                Gmail = new GmailSecrets(
                    account.GmailAddress ?? string.Empty,
                    account.GmailAppPassword ?? string.Empty,
                    account.GmBridgeSession ?? string.Empty)
            }, System.Threading.CancellationToken.None);
        }
        catch
        {
            // Best-effort: the vault picks the credentials up on the next successful save.
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        var account = new AccountStore(this);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(this);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp), PaperTheme.Dp(this, DesignTokens.Space.X12),
            PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp), PaperTheme.Dp(this, DesignTokens.Space.X6));

        // The owner's medallion marks the settings screen (DESIGN.md §13).
        var headerRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        headerRow.SetGravity(GravityFlags.CenterVertical);
        var medallionId = Resources!.GetIdentifier("owner_medallion", "drawable", PackageName);
        if (medallionId != 0)
        {
            var medallion = new ImageView(this);
            medallion.SetImageResource(medallionId);
            var size = PaperTheme.Dp(this, DesignTokens.Shape.TouchTargetDp);
            var medallionParams = new LinearLayout.LayoutParams(size, size)
            {
                MarginEnd = PaperTheme.Dp(this, DesignTokens.Space.X3),
            };
            headerRow.AddView(medallion, medallionParams);
        }
        var heading = PaperUi.PageHeader(this, "Settings");
        heading.SetPadding(0, 0, 0, 0);
        headerRow.AddView(heading);
        layout.AddView(headerRow, Spaced(0, DesignTokens.Space.X6));

        var address = PaperUi.Field(this, "name@gmail.com");
        address.Text = account.GmailAddress;
        AddFieldGroup(layout, "Gmail address", address, null);

        var password = PaperUi.MonoField(this, "");
        password.Text = account.GmailAppPassword;
        password.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;
        password.Typeface = PaperTheme.Mono(this); // setting InputType resets the typeface

        // Show the app-password field only when it's missing or Gmail just rejected it. A working password
        // collapses to a status line with a Replace button — it's backed up to the vault and restored
        // automatically, so there's no need to keep it (a real secret) on display. The field object is always
        // created so Save reads its pre-filled value back even while the field is hidden.
        const string passwordHelper = "Create one at myaccount.google.com/apppasswords.";
        var passwordContainer = new LinearLayout(this) { Orientation = Orientation.Vertical };
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
            ShowPasswordField(passwordState.IsWarning ? passwordState.StatusText : passwordHelper);
        else
            ShowPasswordStatus(passwordState.StatusText);

        var enableMessages = PaperUi.SecondaryButton(this, "Enable message sync (notification access)", () =>
            StartActivity(new Intent(Android.Provider.Settings.ActionNotificationListenerSettings)));
        layout.AddView(enableMessages, Spaced(DesignTokens.Space.X4, 0));

        var note = PaperUi.Secondary(this,
            "Message sync reads incoming texts from Google Messages notifications and " +
            "voicemails (with transcription) from Google Phone notifications. Call and text " +
            "history require the call-log and SMS permissions, which take effect after the " +
            "device is locked down (or a whitelisted install).");
        layout.AddView(note, Spaced(DesignTokens.Space.X6, 0));

        // On a locked-down phone there is no logcat, so surface captured crashes
        // and background failures here where the owner can review and share them.
        var diagnostics = PaperUi.QuietButton(this, "Diagnostics", () =>
            StartActivity(new Intent(this, typeof(DiagnosticsActivity))));
        layout.AddView(diagnostics, Spaced(DesignTokens.Space.X4, 0));

        // Sign in with Google (read-only Drive) so the inbox can greet the owner in their own voice,
        // reading the shared voice profile. Skipping it just keeps neutral copy.
        var voiceStatus = PaperUi.Secondary(this, Codex.Ui.Voice.VoiceGoogleAccount.IsSignedIn
            ? $"Voiced greeting on, signed in as {Codex.Ui.Voice.VoiceGoogleAccount.Email}."
            : "Sign in with Google to be greeted in your own voice — reads your shared voice profile, read-only.");
        var voiceSignIn = PaperUi.SecondaryButton(this, "Sign in with Google", () => _ = VoiceSignInAsync(voiceStatus));
        layout.AddView(voiceSignIn, Spaced(DesignTokens.Space.X6, 0));
        layout.AddView(voiceStatus, Spaced(DesignTokens.Space.X2, 0));

        // Secrets vault: a passphrase encrypts the shared Drive file that backs up the Gmail address, app
        // password, and Messages-for-Web session, so after a factory reset they restore automatically (with
        // the Google sign-in above). Set the same passphrase in each app.
        // Shown in the clear (not a password field) so it can be read back and copied — it's the one secret
        // that isn't kept in the vault, so being able to see it is what lets the owner set the same one everywhere.
        var passphrase = PaperUi.MonoField(this, "Choose a passphrase");
        passphrase.Text = VaultPassphraseStore.Passphrase;
        AddFieldGroup(layout, "Secrets vault passphrase", passphrase,
            "Backs up your Gmail credentials to your Google Drive, encrypted. Restores after a reset. Shown in the clear so you can copy it into your other apps.");

        scroll.AddView(layout);
        root.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        // Success is the state change: saving closes the screen (DESIGN.md §7).
        var save = PaperUi.PrimaryButton(this, "Save", () =>
        {
            account.GmailAddress = address.Text;
            account.GmailAppPassword = password.Text;
            VaultPassphraseStore.Passphrase = passphrase.Text ?? string.Empty;
            // Back up the Gmail credentials to the encrypted Drive vault. Best-effort — they already work
            // locally, so no passphrase/Google/network yet just means the vault picks them up next save.
            PublishGmailToVault(account);
            if (account.IsConfigured)
            {
                // Sync now so conversations appear promptly instead of waiting
                // for the next periodic run.
                EmailSyncWorker.RunOnce(this);
            }
            Finish();
        });
        root.AddView(PaperUi.BottomBar(this, save));

        SetContentView(root);
    }

    private void AddFieldGroup(LinearLayout layout, string label, EditText field, string? helper)
    {
        var labelView = PaperUi.Secondary(this, label);
        labelView.TextSize = DesignTokens.TypeSp.Label;
        labelView.Typeface = PaperTheme.SansSemibold(this);
        labelView.SetTextColor(PaperTheme.Ink);
        layout.AddView(labelView, Spaced(0, DesignTokens.Space.X1));
        layout.AddView(field, Spaced(0, helper is null ? DesignTokens.Space.X4 : DesignTokens.Space.X1));
        if (helper is not null)
        {
            layout.AddView(PaperUi.Secondary(this, helper), Spaced(0, DesignTokens.Space.X4));
        }
    }

    // The collapsed form of a healthy credential: the same field label, then a status line ("Working ✓")
    // with a Replace button that swaps the real field back in for a deliberate rotation.
    private void AddCredentialStatus(LinearLayout layout, string label, string statusText, Action onReplace)
    {
        var labelView = PaperUi.Secondary(this, label);
        labelView.TextSize = DesignTokens.TypeSp.Label;
        labelView.Typeface = PaperTheme.SansSemibold(this);
        labelView.SetTextColor(PaperTheme.Ink);
        layout.AddView(labelView, Spaced(0, DesignTokens.Space.X1));

        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(PaperUi.Secondary(this, statusText),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(PaperUi.QuietButton(this, "Replace", onReplace));
        layout.AddView(row, Spaced(0, DesignTokens.Space.X4));
    }

    private LinearLayout.LayoutParams Spaced(int topDp, int bottomDp) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(this, topDp),
            BottomMargin = PaperTheme.Dp(this, bottomDp),
        };
}
