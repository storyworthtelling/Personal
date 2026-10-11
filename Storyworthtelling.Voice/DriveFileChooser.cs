using DriveData = Google.Apis.Drive.v3.Data;

namespace Storyworthtelling.Voice;

/// <summary>Whether the file a name search settled on belongs to this account, or was shared with it.</summary>
public enum DriveFileOwnership
{
    None,
    Owned,
    Shared,
}

/// <summary>One same-name, non-trashed file a Drive name search returned, reduced to what the rule reads.</summary>
public sealed record DriveFileCandidate(string Id, bool OwnedByMe, DateTimeOffset? ModifiedTime);

/// <summary>The file chosen (or none), whose it is, and how many same-name files were visible.</summary>
public sealed record DriveFileChoice(string? Id, DriveFileOwnership Ownership, int SameNameCount);

/// <summary>
/// Decides which file a well-known name means when Drive shows this account more than one — a stray copy
/// beside the owner's file, or the owner's file beside one shared with them. The search used to take
/// whichever came back first (<c>pageSize=1</c>, no order), so an app could read or write the wrong copy of
/// the vault, the prayer archive or a household file, and could pick differently on the next run.
/// <para>
/// The rule: a file this account owns beats one shared with it; within either group the most recently
/// modified wins; a missing modified time sorts last; an exact tie falls to the lower id, so the answer
/// never depends on listing order. A remembered id (<see cref="IDriveFileIdStore"/>) is checked before any
/// of this, in <see cref="DriveTextFile"/>, so a file already resolved keeps winning while it exists.
/// </para>
/// <para>
/// Fields per Drive v3: <c>ownedByMe</c> ("Whether the user owns the file. Not populated for items in
/// shared drives") and <c>modifiedTime</c> ("The last time the file was modified by anyone"); a
/// <c>name</c> value is single-quoted, with a single quote escaped as <c>\'</c> (Drive search-terms guide).
/// </para>
/// </summary>
public static class DriveFileChooser
{
    /// <summary>The files.list <c>fields</c> mask the rule needs.</summary>
    public const string ListFields = "files(id,ownedByMe,modifiedTime)";

    /// <summary>Enough to see every same-name copy in one page; a well-known name has a handful at most.</summary>
    public const int ListPageSize = 100;

    /// <summary>The files.list <c>q</c> for a well-known name, excluding trashed files.</summary>
    public static string NameQuery(string fileName) =>
        $"name='{fileName.Replace("'", @"\'", StringComparison.Ordinal)}' and trashed=false";

    public static DriveFileChoice Choose(IEnumerable<DriveFileCandidate> candidates)
    {
        var all = candidates.ToList();
        if (all.Count == 0)
        {
            return new DriveFileChoice(null, DriveFileOwnership.None, 0);
        }

        var chosen = all
            .OrderByDescending(c => c.OwnedByMe)
            .ThenByDescending(c => c.ModifiedTime.HasValue)
            .ThenByDescending(c => c.ModifiedTime)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .First();
        var ownership = chosen.OwnedByMe ? DriveFileOwnership.Owned : DriveFileOwnership.Shared;
        return new DriveFileChoice(chosen.Id, ownership, all.Count);
    }

    /// <summary>Reads a files.list result into candidates. An unpopulated <c>ownedByMe</c> is not ownership.</summary>
    public static IReadOnlyList<DriveFileCandidate> FromDrive(IEnumerable<DriveData.File>? files) =>
        files is null
            ? []
            : files
                .Where(f => !string.IsNullOrEmpty(f.Id))
                .Select(f => new DriveFileCandidate(f.Id, f.OwnedByMe == true, f.ModifiedTimeDateTimeOffset))
                .ToList();

    /// <summary>
    /// A diagnostic line when the choice was not clear-cut — several same-name files, or only a shared one —
    /// by count and ownership only: never a name or id, which may be personal. Null when there is nothing to say.
    /// </summary>
    public static string? Notice(DriveFileChoice choice)
    {
        var whose = choice.Ownership == DriveFileOwnership.Owned ? "one this account owns" : "one shared with this account";
        if (choice.SameNameCount > 1)
        {
            return $"{choice.SameNameCount} Drive files share a well-known name; using the most recently modified {whose}.";
        }
        if (choice.Ownership == DriveFileOwnership.Shared)
        {
            return "This account owns no copy of a well-known Drive file; using the one shared with it.";
        }
        return null;
    }
}
