using Codex.Ui;
using Storyworthtelling.Google.Android;
using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Http;
using global::Google.Apis.Services;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using Codex.Ui.Http;
using Storyworthtelling.CallSheet;

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
    private readonly SeatMemoryFile? memory;

    /// <param name="memoryFile">The head's seat memory (the suite's one rule, audit W10.3): a line is written once per
    /// seat, facts and day and kept across a restart. Null composes every time, for a head that keeps its own.</param>
    public VoiceGreeting(Func<Task<string>> accessToken, string applicationName, string? memoryFile = null)
    {
        memory = memoryFile is null ? null : new SeatMemoryFile(memoryFile);
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
            VoiceProfileAccess.ForHead(accessToken, applicationName),
            client,
            warn: line => HeadLog.Warn("Voice", line));
    }

    /// <summary>
    /// Writes a seat on the suite's voice-line Call Sheet in the owner's voice (its purpose, word limit and tier), or
    /// returns "" when it cannot (no profile, no key, no Drive access, a failed call). With a memory file, a line
    /// already written for this seat, subject, context and day is reused, and an empty attempt is believed for
    /// <see cref="SeatPlan.RetryAfter"/> before it is asked again. A screen that stops waiting does not cancel the
    /// call: the line it finishes with is kept for the next time the screen opens.
    /// </summary>
    public async Task<string> ComposeAsync(
        VoiceLine line, string context, CancellationToken cancellationToken, string subject = "")
    {
        var now = DateTimeOffset.Now;
        var today = SeatPlan.Today(now.LocalDateTime);
        var hash = SeatPlan.Hash(line, [context]);
        var remembered = memory?.Get(line, subject);
        switch (SeatPlan.Decide(line, voiceReady: true, hash, hasFacts: true, remembered, now, today))
        {
            case SeatAction.Reuse:
                return remembered!.Line!;
            case SeatAction.Fallback:
                return string.Empty;
        }

        var composed = await ComposeNowAsync(line, context, cancellationToken);
        try
        {
            memory?.Put(line, subject, SeatPlan.Remember(hash, today, composed, now));
        }
        catch (IOException ex)
        {
            HeadLog.Quiet("Voice", "Line not kept", ex);
        }
        return composed;
    }

    /// <summary>
    /// A baseline written in code, said the owner's way (audit W10.2): "" when it cannot be, so the caller keeps
    /// showing the baseline it already drew.
    /// </summary>
    public Task<string> ComposeAsync(Codex.Domain.Voice.VoiceAsk ask, CancellationToken cancellationToken) =>
        ComposeAsync(ask.Line, ask.Context, cancellationToken);

    private async Task<string> ComposeNowAsync(VoiceLine line, string context, CancellationToken cancellationToken)
    {
        try
        {
            await options.RefreshAsync(cancellationToken);
            return await messenger.ComposeAsync(
                new MessageRequest(line.Purpose, context, line.MaxWords, line.HighVisibility, line.Seat),
                fallback: string.Empty, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The key or profile could not be read from Drive; the line hides, and the reason is kept (audit 08 §22).
            HeadLog.Quiet("Voice", "Line not composed", ex);
            return string.Empty;
        }
    }
}
