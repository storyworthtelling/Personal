using Codex.Domain.Music;
using Score.Google;

namespace Score;

/// <summary>
/// Listening Genius's on-device half: the raw signal store, the recorder that turns
/// player ticks into signals, and the throttled fold into the Taste Profile.
///
/// It is one object because the three are never useful apart — every append goes
/// through the recorder, and the refresh reads exactly what the store holds. Pulling
/// them out of the shell also puts the promise in one place: raw events stay on this
/// phone, only the summary leaves, and turning Genius off <b>clears</b> what was
/// gathered rather than merely stopping collection.
///
/// Nothing here is fatal. A refresh that fails leaves the events for the next one.
/// </summary>
public sealed class ListeningLog
{
    // Once a day, near enough. Twenty hours rather than twenty-four so a refresh that
    // happens at the same hour each evening isn't pushed a day later by a few minutes.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(20);

    private readonly ListeningEventStore store;
    private readonly MusicListeningRecorder recorder = new();

    public ListeningLog(Activity host, MusicTasteService? taste)
    {
        store = new ListeningEventStore(host);
        Taste = taste;
        recorder.LearningEnabled = MusicSettingsStore.ListeningGeniusEnabled;
    }

    /// <summary>The Claude-backed profile service, also used to build asked-for playlists.</summary>
    public MusicTasteService? Taste { get; }

    public bool LearningEnabled
    {
        get => recorder.LearningEnabled;
        set => recorder.LearningEnabled = value;
    }

    /// <summary>A new selection started — an album, a shuffle, a smart mix.</summary>
    public void BeginSelection(
        IReadOnlyList<Track> tracks, int index, PlaybackLaunch launch, Func<string, bool> isComplete) =>
        Append(recorder.BeginSelection(tracks, index, launch, isComplete));

    /// <summary>One sample of the player. The recorder decides what that means.</summary>
    public void Observe(string? mediaId, TimeSpan position, TimeSpan duration) =>
        Append(recorder.Observe(mediaId, position, duration));

    /// <summary>Finalize whatever was mid-play — the screen is going away.</summary>
    public void Stop() => Append(recorder.Stop());

    /// <summary>Off means forget what was gathered, not just stop gathering.</summary>
    public void Clear() => store.Clear();

    /// <summary>
    /// Folds the gathered signals into the Taste Profile, at most once a day and off
    /// the UI thread. Silent when Genius is off, when there is nothing to send, or
    /// when the profile was refreshed recently.
    /// </summary>
    public void MaybeRefreshTaste()
    {
        if (!MusicSettingsStore.ListeningGeniusEnabled || Taste is null)
        {
            return;
        }

        if (MusicSettingsStore.TasteLastRefreshUtc is { } when
            && DateTimeOffset.UtcNow - when < RefreshInterval)
        {
            return;
        }

        var events = store.Load();
        if (events.Count == 0)
        {
            return;
        }

        MusicSettingsStore.TasteLastRefreshUtc = DateTimeOffset.UtcNow;
        _ = Taste.RefreshAsync(events, CancellationToken.None);
    }

    private void Append(IReadOnlyList<ListeningEvent> events)
    {
        if (events.Count > 0)
        {
            store.Append(events);
        }
    }
}
