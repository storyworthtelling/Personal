using Codex.Ui;
using Storyworthtelling.Google.Android;
using Android.Content;
using Codex.Ui.Voice;
using Constitution.Domain.Voice;
using Constitution.Infrastructure.Voice;
using Constitution.Services;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using Codex.Ui.Http;

namespace Constitution.BodyTab;

/// <summary>
/// Composes Constitution's seats (Body's month and signal sentences, a workout's encouragement) in the owner's
/// voice by the suite's one rule (<see cref="SeatCache"/> over the seat memory file), or answers null and the
/// seat hides, like every other app's (owner's ruling, W10). Never throws a failure at the page: with no sign-in,
/// no key, no profile or no network the answer is null. Built like the sibling heads' voice stack (<c>VoiceGreeting</c>): Drive reached
/// through the signed-in Google account, the Claude key read from the vault.
/// </summary>
internal static class BodySeats
{
    private const string ApplicationName = "Constitution";

    private static readonly object Gate = new();
    private static SeatCache? cache;

    /// <summary>The line for a request, or null (the seat hides) at once when nobody is signed in to reach the voice with.</summary>
    public static async Task<SeatLine?> ComposeAsync(Context context, SeatRequest request, CancellationToken cancellationToken)
    {
        if (!VoiceGoogleAccount.IsSignedIn)
        {
            return null;
        }

        return await CacheFor(context).ComposeAsync(request, DateTimeOffset.Now, cancellationToken);
    }

    private static SeatCache CacheFor(Context context)
    {
        lock (Gate)
        {
            return cache ??= Build(context.ApplicationContext ?? context);
        }
    }

    private static SeatCache Build(Context context)
    {
        var account = ConstitutionGoogle.Create();
        var driveFactory = GoogleDrive.Factory(account.GetAccessToken, ApplicationName);

        // The Claude key is read from the shared vault with this head's passphrase; with none, the options stay empty and the seat hides.
        var vault = new DriveSecretsVaultStore(new DriveTextFile(driveFactory, DriveSecretsVaultStore.FileName, SharedDriveFileIds.Store, readCache: DriveReadCache.Shared));
        var options = new DriveVoiceOptions(new VaultVoiceConfigStore(vault, () => VaultPassphraseStore.Passphrase));
        var http = HeadHttp.WithTimeout(TimeSpan.FromMinutes(2));
        var messenger = new VoicedMessenger(
            DriveVoiceProfileStore.Sealed(name => new DriveTextFile(driveFactory, name, SharedDriveFileIds.Store, readCache: DriveReadCache.Shared), () => VaultPassphraseStore.Passphrase),
            new VoiceClient(http, options, HeadCredentialHealth.Store()),
            warn: line => HeadLog.Warn("Voice", line));

        // A voiced line is a sentence about the owner's health, so it is kept in the no-backup folder. The old
        // store's file (whole days of lines, keyed another way) is not read again.
        var folder = context.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep its voiced lines in.");
        File.Delete(Path.Combine(folder, "seat-lines.json"));
        return new SeatCache(
            new SeatComposer(messenger, options),
            new Storyworthtelling.CallSheet.SeatMemoryFile(Path.Combine(folder, "voice-seats.json")));
    }

}
