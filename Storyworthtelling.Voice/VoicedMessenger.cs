using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Storyworthtelling.Voice.Claude;

namespace Storyworthtelling.Voice;

// The reusable entry point every app uses to show a situational message in the owner's voice. It loads
// the shared voice profile, asks the voice client to compose the message fresh, and — because generation
// happens live and can legitimately be unavailable (no profile learned yet, no Claude key, offline) —
// falls back to the neutral copy the app would otherwise show. The UI therefore always has something to
// render and this call never throws: a failure here is a graceful degrade to today's wording, not an
// error, which is why it's caught and logged rather than propagated.
public sealed class VoicedMessenger
{
    private readonly IVoiceProfileStore store;
    private readonly IVoiceClient client;
    private readonly ILogger<VoicedMessenger>? logger;
    private readonly Action<string>? warn;

    // A seat's failure is warned once a day per process, not on every paint: the seats compose on each screen
    // visit, and a rejected key would otherwise fill the log with one line per visit.
    private static readonly ConcurrentDictionary<(string Purpose, DateOnly Day), bool> Warned = new();

    /// <param name="warn">For a head with no logging pipeline (the native ones): the same warning as one line.</param>
    public VoicedMessenger(
        IVoiceProfileStore store, IVoiceClient client, ILogger<VoicedMessenger>? logger = null, Action<string>? warn = null)
    {
        this.store = store;
        this.client = client;
        this.logger = logger;
        this.warn = warn;
    }

    // Composes the message for `request` in the owner's voice, or returns `fallback` (the app's neutral
    // copy) when the voice isn't available or generation fails.
    public async Task<string> ComposeAsync(MessageRequest request, string fallback, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await store.LoadAsync(cancellationToken);
            if (profile.IsEmpty)
                return fallback;

            var message = await client.ComposeAsync(profile, request, cancellationToken);
            return string.IsNullOrWhiteSpace(message) ? fallback : message.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Voiced messaging is a nicety layered over neutral copy; a missing key, an offline device, or
            // a transient model error must never break the screen. Degrade to the fallback, note it, move on.
            // At WARN, with what went wrong but never the reply or the message (audit 08 §22): Debug is kept nowhere,
            // so a rejected key on every seat in every app was invisible.
            if (Warned.TryAdd((request.Purpose, DateOnly.FromDateTime(DateTime.Now)), true))
            {
                var line = ClaudeCallLine.Failed($"voice {request.Purpose}", ex);
                logger?.LogWarning("{Line}", line);
                warn?.Invoke(line);
            }
            return fallback;
        }
    }
}
