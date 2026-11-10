using Storyworthtelling.Voice.Claude;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;
using Codex.Domain.Music;

namespace Codex.Infrastructure.Music;

/// <summary>
/// Ties Listening Genius together: reduce the on-device events to evidence, refine the Drive profile through
/// Claude, and save it. The VoicedMessenger analogue — best-effort and non-throwing. With no shared key, no
/// events, or on any client failure it leaves the stored profile untouched, so a keyless or quiet install
/// simply carries on with whatever profile (possibly empty) it already has.
/// </summary>
public sealed class TasteProfileRefresher
{
    private readonly ITasteProfileClient client;
    private readonly ITasteProfileStore store;
    private readonly IOptions<VoiceOptions> optionsAccessor;

    private readonly Action<string>? log;

    /// <param name="log">One line per failed refine (audit 08 §12): the call, the failure kind, status and request id.</param>
    public TasteProfileRefresher(ITasteProfileClient client, ITasteProfileStore store, IOptions<VoiceOptions> options, Action<string>? log = null)
    {
        this.log = log;
        this.client = client;
        this.store = store;
        optionsAccessor = options;
    }

    public async Task<TasteProfile> RefreshAsync(IReadOnlyList<ListeningEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);

        // No shared Claude key -> nothing to refine with; keep whatever profile is stored.
        if (string.IsNullOrWhiteSpace(optionsAccessor.Value.ApiKey))
        {
            return await store.LoadAsync(cancellationToken);
        }

        var evidence = TasteEvidenceBuilder.Build(events);
        var existing = await store.LoadAsync(cancellationToken);
        if (evidence.IsEmpty)
        {
            return existing;
        }

        try
        {
            var updated = await client.RefineAsync(existing, evidence, cancellationToken);
            await store.SaveAsync(updated, cancellationToken);
            return updated;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // A failed refine must never crash the app or clobber a good profile; it is said, though.
            log?.Invoke(ClaudeCallLine.Failed("taste-refine", e));
            return existing;
        }
    }
}
