namespace Codex.Domain.Audio;

/// <summary>Which way a library reconcile should move data.</summary>
public enum LibrarySyncDirection
{
    /// <summary>Local and the Drive mirror already agree — do nothing.</summary>
    None,

    /// <summary>Pull the Drive copy down onto a fresh device (after a reset or on new hardware).</summary>
    Restore,

    /// <summary>Push local up to the Drive mirror.</summary>
    Backup,

    /// <summary>
    /// Take the union of both onto the device and back it up: what a device with shows of its own does before it
    /// has ever read Drive, so its first new show is added to the library instead of replacing it.
    /// </summary>
    Merge
}

/// <summary>
/// Decides which way podcast state should flow between the device and its Drive
/// mirror. The phone is the single source of truth (a future watch talks to the
/// phone, not to Drive), so this is a one-writer backup, not a multi-writer merge:
/// local always wins while it has shows, which lets an unsubscribe propagate rather
/// than be resurrected. Drive only repopulates a device that has no shows of its own.
/// Pure and unit-tested; the Android orchestrator just carries out the verdict.
/// </summary>
public static class AudioLibrarySync
{
    /// <summary>
    /// As below, for an install that has not yet once read Drive (<paramref name="restoreConfirmed"/> false): one
    /// that already has shows merges with the backup rather than overwriting it, because a restore that failed
    /// at first launch (offline, not signed in) would otherwise let the first new show replace the whole library
    /// (error-handling §17). After the first confirmed read the phone is the authority again, so an unsubscribe
    /// propagates rather than being brought back.
    /// </summary>
    public static LibrarySyncDirection Decide(AudioLibrarySnapshot local, AudioLibrarySnapshot? remote, bool restoreConfirmed)
    {
        var direction = Decide(local, remote);
        return !restoreConfirmed && direction == LibrarySyncDirection.Backup && remote is { IsEmpty: false }
            ? LibrarySyncDirection.Merge
            : direction;
    }

    /// <summary>
    /// Both libraries together: shows, listened episodes and resume points from each, with the phone's copy of
    /// anything both hold; settings and the equalizer from the phone unless it has none.
    /// </summary>
    public static AudioLibrarySnapshot Union(AudioLibrarySnapshot local, AudioLibrarySnapshot remote)
    {
        var shows = local.Subscriptions.Concat(remote.Subscriptions)
            .GroupBy(show => show.FeedUrl, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var listened = local.ListenedEpisodeIds.Union(remote.ListenedEpisodeIds, StringComparer.Ordinal).ToList();
        var progress = local.Progress.Concat(remote.Progress)
            .GroupBy(entry => entry.MediaId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        return new AudioLibrarySnapshot(
            shows,
            listened,
            progress,
            local.Settings ?? remote.Settings,
            local.Equalizer ?? remote.Equalizer,
            local.LoudnessMillibels != 0 ? local.LoudnessMillibels : remote.LoudnessMillibels);
    }

    public static LibrarySyncDirection Decide(AudioLibrarySnapshot local, AudioLibrarySnapshot? remote)
    {
        if (remote is null || remote.IsEmpty)
        {
            // Nothing (useful) in the cloud yet: seed it from a device that has state.
            return local.IsEmpty ? LibrarySyncDirection.None : LibrarySyncDirection.Backup;
        }

        // A device with no shows is a fresh one — adopt the cloud copy.
        if (!local.HasSubscriptions)
        {
            return LibrarySyncDirection.Restore;
        }

        // Otherwise local is authoritative; mirror it up only when it has drifted.
        return local.ContentEquals(remote) ? LibrarySyncDirection.None : LibrarySyncDirection.Backup;
    }
}
