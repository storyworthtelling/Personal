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
///
/// <see cref="ToRead"/> is the union of <see cref="ToAdd"/> (the cache has never heard
/// of the id) and <see cref="ToUpdate"/> (the cache has it, under a different version —
/// or, on a <c>rereadAll</c> pass, unconditionally). The split exists for
/// <c>RescanOutcome</c>, which needs "new" counted apart from "changed" to describe a
/// scan's findings in the owner's-voice greeting; <see cref="ToRead"/> alone cannot
/// tell the two apart.
/// </summary>
public sealed record MusicScanPlan(
    IReadOnlyList<string> ToRead, IReadOnlyList<string> ToDelete,
    IReadOnlyList<string> ToAdd, IReadOnlyList<string> ToUpdate)
{
    /// <param name="rereadAll">
    /// Read every file regardless of whether it changed. What this is for: when the
    /// reader learns to read a field it did not read before, every cached row is
    /// missing it and <b>no file has changed to say so</b> — the signatures all still
    /// match. One forced pass fills the gap; without it the new column would stay
    /// empty until each file happened to be edited. A file the cache already knows
    /// under this scheme counts as <see cref="ToUpdate"/> here, not <see cref="ToAdd"/> —
    /// nothing about it is actually new, it is only being re-read.
    /// </param>
    public static MusicScanPlan Compute(
        IReadOnlyDictionary<string, MusicFileVersion> current,
        IReadOnlyDictionary<string, MusicFileVersion> cached,
        bool rereadAll = false)
    {
        var toAdd = new List<string>();
        var toUpdate = new List<string>();
        foreach (var (id, version) in current)
        {
            var known = cached.TryGetValue(id, out var cachedVersion);
            if (!known)
            {
                toAdd.Add(id);
            }
            else if (rereadAll || !cachedVersion.Equals(version))
            {
                toUpdate.Add(id);
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

        return new MusicScanPlan([..toAdd, ..toUpdate], toDelete, toAdd, toUpdate);
    }
}
