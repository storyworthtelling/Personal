namespace Codex.Domain.Audio;

/// <summary>Which way a library reconcile should move data.</summary>
public enum LibrarySyncDirection
{
    /// <summary>Local and the Drive mirror already agree — do nothing.</summary>
    None,

    /// <summary>Pull the Drive copy down onto a fresh device (after a reset or on new hardware).</summary>
    Restore,

    /// <summary>Push local up to the Drive mirror.</summary>
    Backup
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
