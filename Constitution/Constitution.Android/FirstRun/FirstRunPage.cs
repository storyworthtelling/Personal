using Constitution.Domain.Diagnostics;
using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Codex.Ui.Voice;
using Constitution.Domain.Equipment;
using Constitution.Domain.FirstRun;
using Constitution.Domain.Settings;
using Constitution.Services;
using Constitution.Settings;

namespace Constitution.FirstRun;

/// <summary>
/// First run (C-8.3.1, brief First run): not a book page, so plain paper with no crinkle and no
/// scraps, drawn in the same plain idiom as Settings. One step at a time, a status sentence for it, and
/// the step's primary action in the pinned bottom bar. Only three things are asked: Google sign-in, the
/// vault passphrase and the equipment checklist. Health Connect, Sleep Number and the reMarkable are
/// asked when first needed or restored from the vault.
///
/// <para>The passphrase goes to the shared <see cref="VaultPassphraseStore"/> and nowhere else; it is
/// never logged or echoed. Success is the page moving to the next step, never a toast.</para>
/// </summary>
public sealed class FirstRunPage(Activity host, Action changed)
{
    private readonly FirstRunStore store = new(host);
    private readonly EquipmentStore equipmentStore = new(host);
    private readonly VoiceGoogleAccount googleAccount = ConstitutionGoogle.Create();
    private EditText? passphraseField;
    private string? note;

    /// <summary>The shell forwards the account picker's result here.</summary>
    public void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == VoiceGoogleAccount.RequestCode)
        {
            VoiceGoogleAccount.HandleChooserResult(resultCode, data);
        }
    }

    /// <summary>The step still owed, or null when first run is over.</summary>
    public FirstRunStep? Current => FirstRunModel.Current(store.Read());

    public View BuildWell()
    {
        passphraseField = null;
        var step = Current ?? FirstRunStep.Equipment;
        var scroll = new ScrollView(host);
        var column = new LinearLayout(host) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(host, DesignTokens.Shape.ScreenEdgeDp);
        column.SetPadding(edge, 0, edge, 0);

        column.AddView(PaperUi.Label(host, $"Step {FirstRunModel.Steps.ToList().IndexOf(step) + 1} of {FirstRunModel.Steps.Count}"));
        column.AddView(PageFurniture.Sentence(host, FirstRunModel.Sentence(step)));
        if (note is not null)
        {
            column.AddView(PaperUi.Secondary(host, note));
        }

        if (step == FirstRunStep.VaultPassphrase)
        {
            // The clear mono field Settings uses: read back by eye and typed identically into each app.
            passphraseField = PaperUi.MonoField(host, "Choose a passphrase");
            passphraseField.Text = VaultPassphraseStore.Passphrase;
            column.AddView(passphraseField, Spaced());
        }
        else if (step == FirstRunStep.Equipment)
        {
            AddEquipment(column);
        }

        scroll.AddView(column);
        return scroll;
    }

    public View BuildBar()
    {
        var step = Current ?? FirstRunStep.Equipment;
        return PaperUi.BottomBar(host, PaperUi.PrimaryButton(host, FirstRunModel.ActionLabel(step), () => OnAction(step)));
    }

    private void OnAction(FirstRunStep step)
    {
        switch (step)
        {
            case FirstRunStep.GoogleSignIn:
                _ = SignInAsync();
                break;
            case FirstRunStep.VaultPassphrase:
                SavePassphrase();
                break;
            default:
                store.EquipmentConfirmed = true;
                note = null;
                changed();
                break;
        }
    }

    private async Task SignInAsync()
    {
        var signing = TapWatch.Action(ConstitutionKeyActions.GoogleSignIn, TimeSpan.FromMinutes(5));
        var settled = false;
        try
        {
            note = "Choosing an account.";
            changed();
            try
            {
                await googleAccount.SignIn(host);
                signing.Succeeded();
                note = null;
                settled = true;
            }
            catch (Exception e) when (ConnectionTrail.Expecting(host, ConnectionAct.SignIn, e,
                e is InvalidOperationException or OperationCanceledException or TimeoutException or IOException
                    or Java.Lang.Exception))
            {
                signing.Failed(e.GetType().Name);
                note = ConnectionFailure.Sentence(ConnectionAct.SignIn, e, callerCancelled: false);
                settled = true;
                return;
            }

            await RestoreAsync();
        }
        finally
        {
            if (!settled)
            {
                note = "Sign-in stopped unexpectedly. Try again.";
            }

            host.RunOnUiThread(() => changed());
        }
    }

    private void SavePassphrase()
    {
        var typed = passphraseField?.Text?.Trim() ?? string.Empty;
        if (typed.Length == 0)
        {
            note = "A passphrase is needed.";
            changed();
            return;
        }

        VaultPassphraseStore.Passphrase = typed;
        note = null;
        changed();
        _ = RestoreThenRefreshAsync();
    }

    private async Task RestoreThenRefreshAsync()
    {
        await RestoreAsync();
        host.RunOnUiThread(() => changed());
    }

    // The vault may hold what the later steps would ask for; the runner skips quietly when it cannot run.
    private async Task RestoreAsync() =>
        await ConstitutionVaults.Create(host).RestoreAsync(CancellationToken.None);

    private void AddEquipment(LinearLayout column)
    {
        var current = equipmentStore.Read();
        foreach (var item in EquipmentCatalogue.All)
        {
            var line = new LinearLayout(host) { Orientation = Orientation.Horizontal, Clickable = true };
            line.SetGravity(GravityFlags.CenterVertical);
            line.SetMinimumHeight(PaperTheme.Dp(host, DesignTokens.Shape.TouchTargetDp));

            var box = new Checkbox(host);
            box.SetPicked(current.Has(item.Id));
            var size = PaperTheme.Dp(host, DesignTokens.Space.X6);
            line.AddView(box, new LinearLayout.LayoutParams(size, size)
            {
                MarginEnd = PaperTheme.Dp(host, DesignTokens.Space.X3),
            });
            line.AddView(PaperUi.Body(host, item.Name));

            var id = item.Id;
            var picked = current.Has(item.Id);
            line.Click += (_, _) =>
            {
                equipmentStore.Write(equipmentStore.Read().With(id, !picked));
                changed();
            };
            column.AddView(line);
        }
    }

    private LinearLayout.LayoutParams Spaced() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(host, DesignTokens.Space.X3),
        };
}
