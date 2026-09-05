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
/// <para>Plate holds almost nothing of its own — no account, no database, no worker — so
/// the only thing here is the vault passphrase, which is the one credential deliberately
/// <b>not</b> kept in the vault: the cutover's factory reset wipes it, it is re-entered
/// once, and it then unlocks everything else this head needs from Drive. It is shown in
/// the clear rather than masked, because it has to be read back and typed identically into
/// every app, and Android blocks clipboard copy on a password-typed field.</para>
/// </summary>
public sealed class SettingsArea(PlateActivity host) : PlateArea(host)
{
    private EditText? passphrase;

    public override BinderArea Tab =>
        new("Settings", Resource.Drawable.area_settings, PaperTheme.ResolvedMark, PaperTheme.ResolvedFill);

    public override View BuildView()
    {
        var scroll = new ScrollView(Host);
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var gap = PaperTheme.Dp(Host, DesignTokens.Space.X3);

        var label = PaperUi.Label(Host, "Vault passphrase");
        label.SetPadding(0, gap, 0, 0);
        column.AddView(label);

        passphrase = PaperUi.MonoField(Host, "the same words in every app");
        passphrase.Text = VaultPassphraseStore.Passphrase;
        column.AddView(passphrase);
        column.AddView(PaperUi.Secondary(Host,
            "Shown in the clear on purpose: it is the one secret the vault cannot hold, so it "
            + "has to be read back and set identically in every app."));

        var note = PaperUi.Secondary(Host,
            "Plate keeps nothing else. Photos go to your camera roll, scans to Documents.");
        note.SetPadding(0, gap, 0, 0);
        column.AddView(note);

        scroll.AddView(column);
        return scroll;
    }

    public override View BuildBottomBar() =>
        PaperUi.BottomBar(Host, PaperUi.PrimaryButton(Host, "Save", Save));

    private void Save()
    {
        VaultPassphraseStore.Passphrase = passphrase?.Text ?? string.Empty;
        // §7: success is an inline state change. Rebuilding the pane reads the stored value
        // back, which is the state change — there is no toast to say so.
        Invalidate();
    }
}
