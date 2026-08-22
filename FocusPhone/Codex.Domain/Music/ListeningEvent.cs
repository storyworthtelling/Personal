namespace Codex.Domain.Music;

/// <summary>
/// One derived listening observation, flattened with the track's grouping tags so the reduce
/// (<see cref="TasteEvidenceBuilder"/>) needs no catalog lookup. Stored on-device only — the raw
/// history never leaves the phone; only the summarized <see cref="TasteProfile"/> syncs to Drive.
/// </summary>
/// <param name="LocalHour">The owner-local hour (0–23) the play happened, for time-of-day tendencies.</param>
/// <param name="SessionId">Groups events from one listening sitting, so affinity counts distinct
/// sittings rather than raw plays (a repeat-all afternoon must not crown a genre).</param>
public sealed record ListeningEvent(
    string TrackId,
    string Genre,
    string ArtistName,
    string AlbumArtist,
    ListeningSignal Signal,
    int LocalHour,
    string SessionId,
    DateTimeOffset AtUtc);
