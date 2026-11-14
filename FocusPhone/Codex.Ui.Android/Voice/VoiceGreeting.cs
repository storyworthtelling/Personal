using Codex.Ui;
using Storyworthtelling.Google.Android;
using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Http;
using global::Google.Apis.Services;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using Codex.Ui.Http;

namespace Codex.Ui.Voice;

// Builds the owner's-voice stack for a native FocusPhone head and composes a greeting in the owner's
// voice. A head supplies only an access-token getter (from its device-account sign-in); this owns the
// Google Drive glue, the Claude client, and the shared profile/config stores. ComposeAsync degrades to the
// empty string — never throws — when the profile, key, or Drive access isn't there, so a head just hides
// the greeting line and shows nothing extra.
public sealed class VoiceGreeting
{
    private readonly DriveVoiceOptions options;
    private readonly VoicedMessenger messenger;
    private readonly string? cacheFile;

    // One lock for every greeting in the process: a head's areas share one cache file.
    private static readonly object CacheGate = new();
    private const int CacheEntries = 32;

    /// <param name="cacheFile">Where composed lines are kept for their part of the day (efficiency audit §4); null
    /// composes every time.</param>
    public VoiceGreeting(Func<Task<string>> accessToken, string applicationName, string? cacheFile = null)
    {
        this.cacheFile = cacheFile;
        var driveFactory = GoogleDrive.Factory(accessToken, applicationName);

        // The shared Claude key now lives in the encrypted secrets vault (the single source of truth, in
        // place of the old plaintext voice config). VaultVoiceConfigStore reads it back given this head's
        // passphrase; with no passphrase or no key it yields VoiceConfig.Empty and the greeting stays neutral.
        var vaultStore = new DriveSecretsVaultStore(new DriveTextFile(
            driveFactory, DriveSecretsVaultStore.FileName, SharedDriveFileIds.Store, readCache: DriveReadCache.Shared));
        options = new DriveVoiceOptions(new VaultVoiceConfigStore(vaultStore, () => VaultPassphraseStore.Passphrase));
        var http = HeadHttp.WithTimeout(TimeSpan.FromMinutes(2));
        var client = new VoiceClient(http, options, HeadCredentialHealth.Store());
        messenger = new VoicedMessenger(
            DriveVoiceProfileStore.Sealed(name => new DriveTextFile(driveFactory, name, SharedDriveFileIds.Store, readCache: DriveReadCache.Shared), () => VaultPassphraseStore.Passphrase),
            client,
            warn: line => HeadLog.Warn("Voice", line));
    }

    // Composes a greeting in the owner's voice for the occasion, or "" when it can't (no profile, no key,
    // no Drive access, or a call failed). The key is pulled fresh from the shared Drive config first. A
    // greeting is a screen the owner reads, so it defaults to the strong model (highVisibility).
    //
    // A line already composed for this occasion, part of the day and context is reused rather than asked for again
    // (efficiency audit §4; the strong model stays, by the owner's decision). A screen that stops waiting does not
    // cancel the call: the line it finishes with is kept for the next time the screen opens.
    public async Task<string> ComposeAsync(
        string purpose, string context, CancellationToken cancellationToken, bool highVisibility = true)
    {
        var key = Codex.Domain.Voice.GreetingCache.Key(purpose, context, DateTime.Now);
        if (Cached(key) is { } kept)
        {
            return kept;
        }

        try
        {
            await options.RefreshAsync(cancellationToken);
            var line = await messenger.ComposeAsync(
                new MessageRequest(purpose, context, HighVisibility: highVisibility),
                fallback: string.Empty, cancellationToken);
            if (!string.IsNullOrWhiteSpace(line))
            {
                Keep(key, line);
            }
            return line;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The key or profile could not be read from Drive; the greeting hides, and the reason is kept (audit 08 §22).
            HeadLog.Quiet("Voice", "Greeting not composed", ex);
            return string.Empty;
        }
    }


    private string? Cached(string key)
    {
        lock (CacheGate)
        {
            return Read().TryGetValue(key, out var line) ? line : null;
        }
    }

    private void Keep(string key, string line)
    {
        if (cacheFile is null)
        {
            return;
        }

        lock (CacheGate)
        {
            try
            {
                var lines = Read();
                lines.Remove(key);
                var kept = lines.TakeLast(CacheEntries - 1).Append(new KeyValuePair<string, string>(key, line));
                File.WriteAllText(cacheFile, System.Text.Json.JsonSerializer.Serialize(kept.ToDictionary(pair => pair.Key, pair => pair.Value)));
            }
            catch (IOException ex)
            {
                HeadLog.Quiet("Voice", "Greeting not kept", ex);
            }
        }
    }

    // In insertion order, so the oldest go first when the file is full. A missing or damaged file is no cache.
    private Dictionary<string, string> Read()
    {
        if (cacheFile is null || !File.Exists(cacheFile))
        {
            return [];
        }
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(cacheFile)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
