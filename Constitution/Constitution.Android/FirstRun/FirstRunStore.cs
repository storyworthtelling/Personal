using Android.Content;
using Codex.Ui.Voice;
using Constitution.Domain;
using Constitution.Domain.FirstRun;

namespace Constitution.FirstRun;

/// <summary>
/// Reads what is already true of this install for <see cref="FirstRunModel"/>, and keeps the one fact
/// first run owns: that the equipment checklist was explicitly confirmed (the stored equipment reads as
/// everything ticked when never asked, so it cannot say). Sign-in and the passphrase are read from the
/// shared stores; this holds no copy of either.
/// </summary>
public sealed class FirstRunStore(Context context)
{
    private ISharedPreferences Preferences =>
        context.GetSharedPreferences(ConstitutionIdentity.SettingsPreferencesName, FileCreationMode.Private)!;

    public bool EquipmentConfirmed
    {
        get => Preferences.GetBoolean(ConstitutionIdentity.EquipmentConfirmedKey, false);
        set
        {
            using var editor = Preferences.Edit()!;
            editor.PutBoolean(ConstitutionIdentity.EquipmentConfirmedKey, value);
            editor.Apply();
        }
    }

    public FirstRunState Read() =>
        new(VoiceGoogleAccount.IsSignedIn, VaultPassphraseStore.IsSet, EquipmentConfirmed);
}
