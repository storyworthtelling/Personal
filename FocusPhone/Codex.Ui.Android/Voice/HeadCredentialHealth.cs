using global::Android.App;
using global::Android.Content;
using Storyworthtelling.Voice.Health;

namespace Codex.Ui.Voice;

/// <summary>
/// A native head's credential health, in its OWN app-private SharedPreferences (each head is a separate
/// package), through the one shared <see cref="CredentialHealthStore"/> and its lock (audit 01 §14). A head's
/// background work reports Ok/Failing after an authenticated call, and its settings screen reads the report
/// back to hide a working key's field.
/// </summary>
public static class HeadCredentialHealth
{
    public static CredentialHealthStore Store() => new(new SharedPreferencesStorage());

    private sealed class SharedPreferencesStorage : ICredentialHealthStorage
    {
        // Frozen: a phone already holds a report under these names.
        private const string PrefsName = "credentialHealth";
        private const string ReportKey = "report";

        private static ISharedPreferences Prefs() =>
            Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

        public string Read() => Prefs().GetString(ReportKey, string.Empty) ?? string.Empty;

        // Commit, not Apply: the lock in CredentialHealthStore only helps if the next read sees this write.
        public void Write(string report) => Prefs().Edit()!.PutString(ReportKey, report)!.Commit();
    }
}
