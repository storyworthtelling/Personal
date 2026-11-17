using Storyworthtelling.Updates;
using Storyworthtelling.Google.Android;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Codex.Ui.Voice;
using Storyworthtelling.Materials;

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
        new("Settings", TabGlyphs.Settings, PaperTheme.ResolvedMark, PaperTheme.ResolvedFill);

    private SettingsAccordion? accordion;

    public override View BuildView()
    {
        var scroll = new ScrollView(Host);
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var gap = PaperTheme.Dp(Host, DesignTokens.Space.X3);
        // The suite's anatomy (audit W8.4b): Connections, Vault, Diagnostics, then the build line. Plate has no
        // group of its own: it keeps nothing.
        var connections = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var vault = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var diagnostics = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        // The vault passphrase, in the clear on purpose.
        vault.AddView(Label("Vault passphrase", 0));
        passphrase = PaperUi.MonoField(Host, "the same words in every app");
        passphrase.Text = VaultPassphraseStore.Passphrase;
        // There is no Save (§5): stored as it is typed.
        PaperUi.CommitsAsTyped(passphrase, text => VaultPassphraseStore.Passphrase = text);
        vault.AddView(passphrase);
        vault.AddView(PaperUi.Secondary(Host,
            "Shown in the clear on purpose: it is the one secret the vault can’t hold, so it "
            + "has to be read back and set identically in every app."));

        // The Google account, which is here for the vault and nothing else.
        connections.AddView(Label("Google account", 0));
        connections.AddView(PaperUi.Body(Host, VoiceGoogleAccount.IsSignedIn
            ? GoogleSignInHealth.NeedsSignIn()
                ? $"{VoiceGoogleAccount.Email} — Google turned the sign-in away"
                : VoiceGoogleAccount.Email
            : "Not signed in."));
        connections.AddView(PaperUi.SecondaryButton(Host,
            VoiceGoogleAccount.IsSignedIn ? GoogleSignInHealth.ButtonLabel("Use another account") : "Sign in", SignIn));
        connections.AddView(PaperUi.Secondary(Host,
            "Only to read the shared vault. Plate keeps no account of its own, and nothing it "
            + "captures goes to Drive by this route."));
        if (!VoiceGoogleAccount.IsSignedIn)
        {
            // Sign-in needs this app registered as an Android OAuth client (package + signing-cert
            // SHA-1) in the same Google Cloud project the other apps already use. Without this,
            // "Cannot sign in" had no next step — Score and Storyboarder both surface it; Plate
            // never did.
            connections.AddView(PaperUi.Secondary(Host,
                "Setup note: if Google sign-in fails, this build isn’t registered with Google yet. Register it in the Google Cloud project with:\n\n"
                + $"Package: {AppSigningInfo.PackageName}\nSigning fingerprint: {AppSigningInfo.Sha1Fingerprint()}"));
        }

        // The tablet: on the screen only while the pairing needs doing (never paired, or the tablet turned it
        // away). A working pairing is not on the screen at all (owner's ruling, audit W8.4b), and pairing
        // health, not the token's presence, decides (audit 11 §11).
        var pairing = Storyworthtelling.Remarkable.RemarkablePairingStatus.Describe(
            PlateRemarkable.IsPaired,
            HeadCredentialHealth.Store().Load().Get(Storyworthtelling.Remarkable.RemarkableCredentialId.Value),
            "Not paired.");
        if (pairing.NeedsPairing)
        {
            connections.AddView(Label("reMarkable", gap));
            if (PlateRemarkable.IsPaired)
            {
                connections.AddView(PaperUi.Body(Host, pairing.Line));
            }
            connections.AddView(PaperUi.Secondary(Host,
                "Open my.remarkable.com, choose to connect a device, and type the one-time code."));
            pairingCode = PaperUi.MonoField(Host, "one-time code");
            connections.AddView(pairingCode);
            KeyConsoleLink.AddTo(connections, Storyworthtelling.Voice.Health.SharedCredentialIds.Remarkable);
            connections.AddView(PaperUi.SecondaryButton(Host, pairing.Button, Pair));
        }

        status = PaperUi.Secondary(Host, "");
        status.Visibility = ViewStates.Gone;
        status.SetPadding(0, gap, 0, gap);
        connections.AddView(status);

        // Diagnostics: the failure log, the weekly upload and Send logs now (LOG-LOOP-PLAN.md §3).
        diagnostics.AddView(PaperUi.SecondaryButton(Host, "Open diagnostics", () => LoopDiagnosticsActivity.Open(Host)));

        // Connections starts open: sign-in and pairing outcomes are said there, under the button that caused them.
        accordion ??= new SettingsAccordion(Host, Invalidate, SettingsAnatomy.Connections);
        accordion.Draw(column,
        [
            new SettingsSection(SettingsAnatomy.Connections, null, body => body.AddView(connections)),
            new SettingsSection(SettingsAnatomy.Vault, null, body => body.AddView(vault)),
            new SettingsSection(SettingsAnatomy.Diagnostics, null, body => body.AddView(diagnostics)),
        ]);

        var note = PaperUi.Secondary(Host,
            "Nothing leaves Plate until you send it. Until then the Darkroom holds the only copy.");
        note.SetPadding(0, gap, 0, gap);
        column.AddView(note);

        // The quiet foot line: which build is actually on the phone. The channel installs
        // silently and most updates change nothing visible, so without it "did that land?"
        // has no answer on the device.
        var buildLine = PaperUi.BuildNumber(Host, "Plate");
        var buildParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        buildParams.TopMargin = PaperTheme.Dp(Host, DesignTokens.Space.X8);
        column.AddView(buildLine, buildParams);

        scroll.AddView(column);
        return scroll;
    }

    private TextView Label(string text, int gap)
    {
        var label = PaperUi.Label(Host, text);
        label.SetPadding(0, gap, 0, 0);
        return label;
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
            ShowStatus($"Couldn’t sign in: {Storyworthtelling.Diagnostics.PlainError.Describe(e, "Google")}. If this build is new, see the setup note under Connections.");
        }
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
            ShowStatus($"That code was refused: {Storyworthtelling.Diagnostics.PlainError.Describe(e, "reMarkable")}");
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
