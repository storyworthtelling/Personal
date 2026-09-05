using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Codex.Ui.Voice;

namespace Plate;

/// <summary>
/// Settings. Every app in the suite has this area and it is the one that looks the same in
/// all of them, which is why its tab is the neutral grey rather than an identity or role
/// hue (§9), and why its glyph now lives once in the shared kit rather than being copied a
/// third time.
///
/// <para>Plate holds almost nothing of its own — no account of its own, no database, no
/// worker. There are three things here and each earns its place: the vault passphrase,
/// which is the one credential deliberately <b>not</b> kept in the vault; the Google
/// sign-in, which exists only to reach that vault; and the reMarkable pairing, which is
/// Scan's third door.</para>
/// </summary>
public sealed class SettingsArea(PlateActivity host) : PlateArea(host)
{
    private readonly PlateActivity shell = host;

    private EditText? passphrase;
    private EditText? pairingCode;
    private TextView? status;

    public override BinderArea Tab =>
        new("Settings", Resource.Drawable.area_settings, PaperTheme.ResolvedMark, PaperTheme.ResolvedFill);

    public override View BuildView()
    {
        var scroll = new ScrollView(Host);
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var gap = PaperTheme.Dp(Host, DesignTokens.Space.X3);

        // The vault passphrase, in the clear on purpose.
        column.AddView(Label("Vault passphrase", gap));
        passphrase = PaperUi.MonoField(Host, "the same words in every app");
        passphrase.Text = VaultPassphraseStore.Passphrase;
        column.AddView(passphrase);
        column.AddView(PaperUi.Secondary(Host,
            "Shown in the clear on purpose: it is the one secret the vault cannot hold, so it "
            + "has to be read back and set identically in every app."));

        // The Google account, which is here for the vault and nothing else.
        column.AddView(Label("Google account", gap));
        column.AddView(PaperUi.Body(Host, VoiceGoogleAccount.IsSignedIn
            ? VoiceGoogleAccount.Email
            : "Not signed in."));
        column.AddView(PaperUi.SecondaryButton(Host,
            VoiceGoogleAccount.IsSignedIn ? "Use another account" : "Sign in", SignIn));
        column.AddView(PaperUi.Secondary(Host,
            "Only to read the shared vault. Plate keeps no account of its own, and nothing it "
            + "captures goes to Drive by this route."));

        // The tablet.
        column.AddView(Label("reMarkable", gap));
        if (PlateRemarkable.IsPaired)
        {
            // §3: a working credential is a done state, not a field to keep on screen.
            column.AddView(PaperUi.Body(Host, "Paired ✓"));
            column.AddView(PaperUi.Secondary(Host,
                "Scans sent here land at the root of the tablet."));
            column.AddView(PaperUi.SecondaryButton(Host, "Pair again", ShowPairingField));
        }
        else
        {
            column.AddView(PaperUi.Secondary(Host,
                "Open my.remarkable.com, choose to connect a device, and type the one-time code."));
            pairingCode = PaperUi.MonoField(Host, "one-time code");
            column.AddView(pairingCode);
            column.AddView(PaperUi.SecondaryButton(Host, "Pair", Pair));
        }

        status = PaperUi.Secondary(Host, "");
        status.Visibility = ViewStates.Gone;
        status.SetPadding(0, gap, 0, gap);
        column.AddView(status);

        var note = PaperUi.Secondary(Host,
            "Plate keeps nothing else. Photos go to your camera roll, scans to Documents.");
        note.SetPadding(0, gap, 0, gap);
        column.AddView(note);

        scroll.AddView(column);
        return scroll;
    }

    public override View BuildBottomBar() =>
        PaperUi.BottomBar(Host, PaperUi.PrimaryButton(Host, "Save", Save));

    private TextView Label(string text, int gap)
    {
        var label = PaperUi.Label(Host, text);
        label.SetPadding(0, gap, 0, 0);
        return label;
    }

    private void Save()
    {
        VaultPassphraseStore.Passphrase = passphrase?.Text ?? string.Empty;
        // §7: success is an inline state change. Rebuilding the pane reads the stored value
        // back, which is the state change — there is no toast to say so.
        Invalidate();
    }

    private async void SignIn()
    {
        try
        {
            await shell.Account.SignIn(Host);
            // A fresh sign-in is the first moment the vault is reachable, so this is where a
            // reset gets its reMarkable pairing back.
            await shell.Remarkable.RestoreFromVault();
            Invalidate();
        }
        catch (Exception e)
        {
            ShowStatus($"Could not sign in: {e.Message}");
        }
    }

    private void ShowPairingField()
    {
        PlateRemarkable.DeviceToken = string.Empty;
        Invalidate();
    }

    private async void Pair()
    {
        var code = pairingCode?.Text?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            ShowStatus("Type the code from my.remarkable.com first.");
            return;
        }

        ShowStatus("Pairing…");
        try
        {
            await PlateRemarkable.Pair(code);
            // Back it up so the other apps inherit the pairing — one pairing serves all of
            // them, which is the whole reason the token lives in the vault.
            await shell.Remarkable.PublishToVault();
            Invalidate();
        }
        catch (Exception e)
        {
            ShowStatus($"That code was refused: {e.Message}");
        }
    }

    private void ShowStatus(string message)
    {
        if (status is null)
        {
            return;
        }
        status.Text = message;
        status.Visibility = ViewStates.Visible;
    }
}
