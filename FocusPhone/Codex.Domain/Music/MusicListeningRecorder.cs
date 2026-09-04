namespace Codex.Domain.Music;

/// <summary>
/// Turns the music player's per-tick state into <see cref="ListeningEvent"/>s. Deliberately driven by
/// polling (the Android head samples the controller each second) rather than deep Media3 transition
/// callbacks: when the playing track id changes, the outgoing track is finalized into a
/// <see cref="TrackPlay"/> whose completed-vs-skipped outcome is decided by how far it got — so both a
/// natural auto-advance and a manual early skip are read correctly. Stateful but pure and unit-tested;
/// the Android side only feeds it readings and persists what it returns.
/// </summary>
public sealed class MusicListeningRecorder
{
    // Within this window of the end, a track that hands off to the next one counts as finished even if the
    // last sampled position was a beat short of the true end (ticker granularity + the player advancing).
    private static readonly TimeSpan EndWindow = TimeSpan.FromSeconds(2);

    private readonly Func<DateTimeOffset> now;

    private IReadOnlyList<Track> selection = [];
    private PlaybackLaunch launch;
    private string sessionId = string.Empty;
    private string? currentId;
    private TimeSpan lastPosition;
    private TimeSpan duration;
    private bool wasAlreadyComplete;
    private bool hasInFlight;

    public MusicListeningRecorder(Func<DateTimeOffset>? clock = null) => now = clock ?? (() => DateTimeOffset.Now);

    /// <summary>The kill switch — off, the recorder finalizes nothing and derives no events.</summary>
    public bool LearningEnabled { get; set; } = true;

    /// <summary>
    /// Starts a new listening sitting for a freshly-launched selection. Any track still in flight from a
    /// prior selection is finalized first (a switch). <paramref name="wasComplete"/> resolves whether the
    /// starting track is a replay of something already finished.
    /// </summary>
    public IReadOnlyList<ListeningEvent> BeginSelection(
        IReadOnlyList<Track> tracks, int startIndex, PlaybackLaunch launch, Func<string, bool> wasComplete)
    {
        var carried = FinalizeCurrent();

        selection = tracks;
        this.launch = launch;
        sessionId = Guid.NewGuid().ToString("N");

        var startId = startIndex >= 0 && startIndex < tracks.Count ? tracks[startIndex].Id : null;
        currentId = startId;
        lastPosition = TimeSpan.Zero;
        duration = startId is null ? TimeSpan.Zero : tracks[startIndex].Duration;
        wasAlreadyComplete = startId is not null && wasComplete(startId);
        hasInFlight = startId is not null;

        return carried;
    }

    /// <summary>Feeds one sample of the player state; finalizes the previous track when the id changes.</summary>
    public IReadOnlyList<ListeningEvent> Observe(string? mediaId, TimeSpan position, TimeSpan trackDuration)
    {
        if (!LearningEnabled)
        {
            currentId = mediaId;
            hasInFlight = false;
            return [];
        }

        if (mediaId != currentId)
        {
            var carried = FinalizeCurrent();
            currentId = mediaId;
            lastPosition = position;
            duration = trackDuration > TimeSpan.Zero ? trackDuration : ResolveDuration(mediaId);
            wasAlreadyComplete = false; // an auto-advanced track is not a replay
            hasInFlight = mediaId is not null;
            return carried;
        }

        lastPosition = position;
        if (trackDuration > TimeSpan.Zero)
        {
            duration = trackDuration;
        }

        return [];
    }

    /// <summary>Finalizes the in-flight track — call when playback stops or the app leaves the foreground.</summary>
    public IReadOnlyList<ListeningEvent> Stop() => FinalizeCurrent();

    private IReadOnlyList<ListeningEvent> FinalizeCurrent()
    {
        if (!LearningEnabled || !hasInFlight || currentId is null)
        {
            Reset();
            return [];
        }

        var track = selection.FirstOrDefault(t => t.Id == currentId);
        if (track is null)
        {
            Reset();
            return [];
        }

        var trackDuration = duration > TimeSpan.Zero ? duration : track.Duration;
        var reachedEnd = trackDuration > TimeSpan.Zero && lastPosition >= trackDuration - EndWindow;
        var timestamp = now();
        var play = new TrackPlay(
            track, launch, lastPosition, trackDuration, reachedEnd, wasAlreadyComplete,
            timestamp.Hour, sessionId, timestamp);

        var events = SignalDeriver.Derive(play, LearningEnabled);
        Reset();
        return events;
    }

    private TimeSpan ResolveDuration(string? mediaId) =>
        selection.FirstOrDefault(t => t.Id == mediaId)?.Duration ?? TimeSpan.Zero;

    private void Reset()
    {
        currentId = null;
        hasInFlight = false;
        lastPosition = TimeSpan.Zero;
        duration = TimeSpan.Zero;
        wasAlreadyComplete = false;
    }
}
