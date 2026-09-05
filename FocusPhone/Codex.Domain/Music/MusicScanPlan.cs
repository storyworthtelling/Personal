namespace Codex.Domain.Music;

/// <summary>
/// A file's change signature: its Drive modified-time and size. Two scans that see
/// the same signature for an id can assume the tags are unchanged and skip re-reading
/// the file — the basis of the incremental scan.
/// </summary>
public readonly record struct MusicFileVersion(long ModifiedTimeTicks, long SizeBytes);

/// <summary>
/// The work an incremental library scan actually has to do, computed by diffing the
/// current Drive listing against the versions already in the cache: which files to
/// (re-)read tags for, and which cached tracks to drop because their file is gone.
/// Files present in both with an identical version are left untouched. Pure, so it is
/// unit-tested; the Android scanner just carries out the plan.
/// </summary>
public sealed record MusicScanPlan(IReadOnlyList<string> ToRead, IReadOnlyList<string> ToDelete)
{
    /// <param name="rereadAll">
    /// Read every file regardless of whether it changed. What this is for: when the
    /// reader learns to read a field it did not read before, every cached row is
    /// missing it and <b>no file has changed to say so</b> — the signatures all still
    /// match. One forced pass fills the gap; without it the new column would stay
    /// empty until each file happened to be edited.
    /// </param>
    public static MusicScanPlan Compute(
        IReadOnlyDictionary<string, MusicFileVersion> current,
        IReadOnlyDictionary<string, MusicFileVersion> cached,
        bool rereadAll = false)
    {
        var toRead = new List<string>();
        foreach (var (id, version) in current)
        {
            if (rereadAll || !cached.TryGetValue(id, out var known) || !known.Equals(version))
            {
                toRead.Add(id);
            }
        }

        var toDelete = new List<string>();
        foreach (var id in cached.Keys)
        {
            if (!current.ContainsKey(id))
            {
                toDelete.Add(id);
            }
        }

        return new MusicScanPlan(toRead, toDelete);
    }
}
