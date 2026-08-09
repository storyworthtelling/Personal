using global::Android.App;
using global::Android.Content;

namespace FocusPhone.Ui.Voice;

// The passphrase that encrypts the shared Drive secrets vault (Storyworthtelling.Voice.Secrets), stored in
// the head's OWN app-private SharedPreferences. It is the one credential deliberately NOT kept in the vault —
// wiped by the cutover's factory reset and re-entered once per head afterwards, which then unlocks restoring
// that head's keys from Drive. Each FocusPhone head is a separate package with isolated prefs, so the owner
// sets it once in each head that holds secrets (Communication, Launcher, Audio).
public static class VaultPassphraseStore
{
    private const string PrefsName = "vault";
    private const string PassphraseKey = "passphrase";

    private static ISharedPreferences Prefs() =>
        Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

    public static string Passphrase
    {
        get => Prefs().GetString(PassphraseKey, string.Empty) ?? string.Empty;
        set
        {
            var editor = Prefs().Edit()!;
            editor.PutString(PassphraseKey, value);
            editor.Apply();
        }
    }

    public static bool IsSet => !string.IsNullOrEmpty(Passphrase);
}
