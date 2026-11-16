using Storyworthtelling.Google.Android;
using Storyworthtelling.Voice;

namespace Codex.Ui.Voice;

/// <summary>
/// The shared voice profile for a native head: the sealed Drive file every app reads, opened with this
/// head's vault passphrase. <see cref="VoiceGreeting"/> reads it through the messenger; a seat with its own
/// call (Plate's caption) is handed it as an <see cref="IVoiceProfileSource"/>, the read half only.
/// </summary>
public static class VoiceProfileAccess
{
    public static IVoiceProfileStore ForHead(Func<Task<string>> accessToken, string applicationName)
    {
        var driveFactory = GoogleDrive.Factory(accessToken, applicationName);
        return DriveVoiceProfileStore.Sealed(
            name => new DriveTextFile(driveFactory, name, SharedDriveFileIds.Store, readCache: DriveReadCache.Shared),
            () => VaultPassphraseStore.Passphrase);
    }
}
