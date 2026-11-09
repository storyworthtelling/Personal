using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Maui.Health;

/// <summary>
/// A MAUI head's credential health, in its MAUI Preferences, through the one shared
/// <see cref="CredentialHealthStore"/> and its lock (audit 01 §14). Index, Folio and Foreword each kept a
/// byte-identical copy of this with no lock.
/// </summary>
public static class MauiCredentialHealth
{
    public static CredentialHealthStore Store() => new(new PreferencesStorage());

    private sealed class PreferencesStorage : ICredentialHealthStorage
    {
        // Frozen: all three heads already hold a report under this key.
        private const string Key = "CredentialHealthReport";

        public string Read() => Preferences.Get(Key, string.Empty);

        public void Write(string report) => Preferences.Set(Key, report);
    }
}
