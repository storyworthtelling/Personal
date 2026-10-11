using Android.App;
using Android.Content;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Codex.Ui.Voice;
using Pulp.Domain.Actions;
using Pulp.Domain.FirstRun;
using Pulp.Domain.Shell;
using Pulp.App;
using Pulp.Domain.Core;
using Pulp.Components;
using Pulp.Shell;
using Storyworthtelling.Voice.Health;

namespace Pulp.Vault;

/// <summary>
/// The Vault (D142, D272 — built from the Paper anatomy, no mockup): the Google account the vault is
/// found through, the passphrase that opens it (in the clear — it is the one secret not kept in the
/// vault, and has to be read back letter by letter against the other apps), and Pulp's three keys,
/// each through <see cref="CredentialFieldState"/>: a missing key is a field; a rejected one is a field
/// with one plain sentence; a working one collapses to its status with Replace. Save publishes Pulp's
/// section alone and has the services take the keys up. Success is the page changing; a failure folds
/// into the dog-ear and is never voiced.
/// </summary>
[Activity(Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class VaultActivity : Activity
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, EditText> keyFields = [];
    private readonly HashSet<string> replacing = [];

    private PulpServices services = null!;
    private PulpVault vault = null!;
    private FrameLayout overlay = null!;
    private LinearLayout body = null!;
    private TextView status = null!;
    private EditText passphrase = null!;
    private Button save = null!;
    private bool busy;

    public static Intent For(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var intent = new Intent(context, typeof(VaultActivity));
        return intent;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        services = PulpServices.For(this);
        vault = new PulpVault(services);
        SetContentView(BuildLayout());
        Paint();
    }

    protected override void OnDestroy()
    {
        lifetime.Cancel();
        base.OnDestroy();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == VoiceGoogleAccount.RequestCode)
        {
            VoiceGoogleAccount.HandleChooserResult(resultCode, data);
        }
    }

    // ---- layout ----

    private View BuildLayout()
    {
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, PaperTheme.Dp(this, DesignTokens.Space.X6));

        content.AddView(PaperUi.ScreenHeader(this, VaultPageText.Title, AreaPalette.For(PulpAreas.RuleHue(PulpArea.Settings)).Rule));

        status = PaperUi.Secondary(this, string.Empty);
        status.Visibility = ViewStates.Gone;
        content.AddView(status, Spaced(0, DesignTokens.Space.X4));

        // The passphrase field is built once so what the owner typed survives a repaint.
        passphrase = PaperUi.MonoField(this, VaultPageText.PassphraseHint);
        passphrase.Text = VaultPassphraseStore.Passphrase;

        body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.AddView(body);

        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(content);
        overlay = PaperUi.WrapForTopInset(this, scroll);

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.AddView(overlay, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        save = PaperUi.PrimaryButton(this, VaultPageText.Save, () => _ = SaveAsync());
        column.AddView(PaperUi.BottomBar(this, save));
        return column;
    }

    private void Paint()
    {
        body.RemoveAllViews();

        // The account.
        body.AddView(PaperUi.Heading(this, VaultPageText.AccountHeading), Spaced(DesignTokens.Space.X4, DesignTokens.Space.X2));
        body.AddView(PaperUi.Secondary(this, VoiceGoogleAccount.IsSignedIn
            ? VaultPageText.SignedInAs(VoiceGoogleAccount.Email)
            : VaultPageText.NotSignedIn), Spaced(0, DesignTokens.Space.X2));
        body.AddView(PaperUi.SecondaryButton(this,
            VoiceGoogleAccount.IsSignedIn ? VaultPageText.SignInAgain : VaultPageText.SignIn,
            () => _ = SignInAsync()), Spaced(0, DesignTokens.Space.X6));

        // The passphrase, in the clear.
        body.AddView(PaperUi.Heading(this, VaultPageText.PassphraseHeading), Spaced(0, DesignTokens.Space.X2));
        Detach(passphrase);
        body.AddView(passphrase, Spaced(0, DesignTokens.Space.X2));
        body.AddView(PaperUi.Secondary(this, VaultPageText.PassphraseHelper), Spaced(0, DesignTokens.Space.X6));

        // The keys.
        body.AddView(PaperUi.Heading(this, VaultPageText.KeysHeading), Spaced(0, DesignTokens.Space.X2));
        var report = new CredentialHealthStore().Load();
        foreach (var key in VaultKeys.All)
        {
            PaintKey(key, report.Get(key.CredentialId));
        }
    }

    private void PaintKey(VaultKey key, CredentialHealth health)
    {
        var rejected = VaultKeys.StillRejected(
            health.Status == CredentialStatus.Failing, health.CheckedAtUtc, PulpVault.ReplacedAt(key.CredentialId));

        // A key replaced since its rejection is simply not checked yet, which the shared rule reads as Unknown.
        var effective = health.Status == CredentialStatus.Failing && !rejected ? CredentialHealth.Unknown : health;
        var state = CredentialFieldState.Decide(!string.IsNullOrEmpty(vault.ValueOf(key.CredentialId)), effective);

        body.AddView(PaperUi.Label(this, key.Name), Spaced(DesignTokens.Space.X2, DesignTokens.Space.X1));

        if (state.ShowEntry || replacing.Contains(key.CredentialId))
        {
            var field = FieldFor(key);
            body.AddView(field, Spaced(0, DesignTokens.Space.X1));

            // The shared rule's own wording for a rejection carries the service's message; the page
            // says it in one plain sentence instead.
            var helper = state.IsWarning ? VaultKeys.Rejected(key.Name) : VaultPageText.KeyHelper;
            var line = PaperUi.Secondary(this, helper);
            if (state.IsWarning)
            {
                line.SetTextColor(PaperTheme.ProblemMark);
            }

            body.AddView(line, Spaced(0, DesignTokens.Space.X4));
            return;
        }

        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(PaperUi.Body(this, state.StatusText), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(PaperUi.QuietButton(this, VaultPageText.Replace, () =>
        {
            replacing.Add(key.CredentialId);
            Paint();
            FieldFor(key).RequestFocus();
        }));
        body.AddView(row, Spaced(0, DesignTokens.Space.X4));
    }

    // One field per key for the page's life, so a typed key survives a repaint. Masked like the suite's
    // other service secrets; only the passphrase is in the clear.
    private EditText FieldFor(VaultKey key)
    {
        if (!keyFields.TryGetValue(key.CredentialId, out var field))
        {
            field = PaperUi.MonoField(this, VaultPageText.KeyHint);
            field.InputType = InputTypes.ClassText | InputTypes.TextVariationPassword;
            field.Typeface = PaperTheme.Mono(this); // setting InputType resets the typeface
            keyFields[key.CredentialId] = field;
        }

        Detach(field);
        return field;
    }

    // ---- actions ----

    private async Task SignInAsync()
    {
        try
        {
            await vault.SignInAsync(this);
            PulpNotices.Withdraw(this, overlay, VaultOutcomes.SignInKey);
            Paint();
            await RestoreAsync();
        }
        catch (Exception exception) when (CancellationVerdict.IsUserCancel(exception, lifetime.Token))
        {
            // The page closed mid-sign-in; there is no page left to tell.
            return;
        }
        catch (Exception exception)
        {
            services.Diagnostics.Warn("vault", "Google sign-in did not complete.", exception);
            ShowStatus(null);
            Raise(VaultOutcomes.SignInFailed(DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Save: the passphrase is kept on this phone, then the keys typed are published — or, with none
    /// typed, the vault is simply read, which is how a passphrase entered here brings the keys back.
    /// </summary>
    private async Task SaveAsync()
    {
        if (busy)
        {
            return;
        }

        busy = true;
        save.Enabled = false;
        try
        {
            VaultPassphraseStore.Passphrase = passphrase.Text ?? string.Empty;
            var entered = keyFields
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Text))
                .ToDictionary(pair => pair.Key, pair => pair.Value.Text!);

            if (entered.Count == 0)
            {
                await RestoreAsync();
                return;
            }

            ShowStatus(VaultPageText.Saving);
            var outcome = await vault.SaveAsync(entered, lifetime.Token);
            Settle(VaultOutcomes.Sentence(outcome), VaultOutcomes.Notice(outcome, DateTimeOffset.UtcNow));
            if (outcome == VaultSaveOutcome.Saved)
            {
                foreach (var id in entered.Keys)
                {
                    keyFields[id].Text = string.Empty;
                    replacing.Remove(id);
                    PulpNotices.Withdraw(this, overlay, KeyNoticeKey(id));
                }
            }

            Paint();
        }
        catch (Exception exception) when (CancellationVerdict.IsUserCancel(exception, lifetime.Token))
        {
            // The page closing mid-save leaves no page to tell.
            return;
        }
        catch (Exception exception)
        {
            // Anything else — a timeout included — folds into the dog-ear and un-sticks the page.
            services.Diagnostics.Warn("vault", "Saving the Vault page failed.", exception);
            Settle(null, VaultOutcomes.Notice(VaultSaveOutcome.WriteFailed, DateTimeOffset.UtcNow));
        }
        finally
        {
            busy = false;
            save.Enabled = true;
        }
    }

    private async Task RestoreAsync()
    {
        ShowStatus(VaultPageText.Opening);
        var outcome = await vault.RestoreAsync(lifetime.Token);
        Settle(VaultOutcomes.Sentence(outcome, vault.KeysPresent()), VaultOutcomes.Notice(outcome, DateTimeOffset.UtcNow));
        Paint();
    }

    // A sentence is the state change; a notice is the failure, and the sentence line goes quiet.
    private void Settle(string? sentence, WaitingNotice? notice)
    {
        if (notice is null)
        {
            PulpNotices.Withdraw(this, overlay, VaultOutcomes.NoticeKey);
        }
        else
        {
            Raise(notice);
        }

        ShowStatus(sentence);
    }

    private void Raise(WaitingNotice notice) =>
        PulpNotices.Raise(this, overlay, notice, new PulpNotices.Buttons(OpenVault: () => passphrase.RequestFocus()));

    private void ShowStatus(string? sentence)
    {
        status.Text = sentence ?? string.Empty;
        status.Visibility = string.IsNullOrEmpty(sentence) ? ViewStates.Gone : ViewStates.Visible;
    }

    // The rejected-key notice's own key, asked of the notice rather than restated.
    private static string KeyNoticeKey(string credentialId) =>
        WaitingNotices.KeyRejected(credentialId, credentialId, DateTimeOffset.UnixEpoch).Key;

    private static void Detach(View view) => (view.Parent as ViewGroup)?.RemoveView(view);

    private LinearLayout.LayoutParams Spaced(int topSpace, int bottomSpace) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(this, topSpace),
            BottomMargin = PaperTheme.Dp(this, bottomSpace),
        };
}
