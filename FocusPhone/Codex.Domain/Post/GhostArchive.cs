using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>A filed message, and when the owner dealt with it.</summary>
public sealed record Ghost(TimelineItem Item, DateTimeOffset FiledAtUtc);

/// <summary>
/// What filing keeps. Once a message is deleted at the source, the ghost here is the only
/// copy that exists — which is why the archive is a <b>separate store</b> from the live
/// cache rather than a flag on it. The two answer to different masters: the cache mirrors
/// the source and is rebuilt from it whenever convenient, while the archive must never be
/// rebuilt from anything, because there is nothing left to rebuild it from.
///
/// Filed is ordered by when the owner dealt with something rather than by when it arrived.
/// Those are different questions, and on a to-do list the first is the useful one.
/// </summary>
public sealed class GhostArchive
{
    private readonly Dictionary<string, Ghost> ghosts = [];

    /// <summary>Every ghost, most recently filed first.</summary>
    public IReadOnlyList<Ghost> All =>
        ghosts.Values.OrderByDescending(ghost => ghost.FiledAtUtc).ToList();

    public void File(TimelineItem item, DateTimeOffset filedAtUtc)
    {
        // Filing the same thing twice is one ghost: a retried sync must not double the
        // archive, and the first filing is the one that says when it was dealt with.
        if (!ghosts.ContainsKey(item.SourceId))
        {
            ghosts[item.SourceId] = new Ghost(item, filedAtUtc);
        }
    }

    /// <summary>
    /// Takes a message back out of Filed. What comes back is the ghost, because the source
    /// original is already gone — there is nothing else it could be.
    /// </summary>
    public TimelineItem? Unfile(string sourceId)
    {
        if (!ghosts.Remove(sourceId, out var ghost))
        {
            return null;
        }
        return ghost.Item;
    }

    /// <summary>
    /// Ghosts matching a filter. One filter spans the Mailbox and Filed alike (§7), so
    /// someone filed last week is still findable without remembering that you filed them.
    /// </summary>
    /// <summary>
    /// What <b>Filed</b> shows: ghosts matching the query whose undo window has closed.
    ///
    /// A message filed a second ago is deliberately in neither list — gone from the
    /// Mailbox, not yet in Filed. Seeing it arrive in Filed while the fold is still
    /// offering to put it back reads as the app having already decided, and the owner
    /// said as much: it "does read as broken".
    ///
    /// This hides a ghost from one screen. It never withholds one from <see cref="All"/>,
    /// which is what the archive, the Drive backup and the owed-delete retry read: the
    /// live row is deleted the moment filing happens, so inside that window the ghost is
    /// the only copy of the message anywhere.
    /// </summary>
    public IReadOnlyList<Ghost> Settled(string query, DateTimeOffset now, TimeSpan undoWindow) =>
        Matching(query)
            .Where(ghost => ghost.FiledAtUtc + undoWindow <= now)
            .ToList();

    public IReadOnlyList<Ghost> Matching(string query)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return All;
        }

        return All.Where(ghost =>
            Contains(ghost.Item.ContactKey, trimmed)
            || Contains(ghost.Item.Subject, trimmed)
            || Contains(ghost.Item.Snippet, trimmed)
            || Contains(ghost.Item.Body, trimmed)).ToList();
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public static GhostArchive From(IEnumerable<Ghost> ghosts)
    {
        var archive = new GhostArchive();
        foreach (var ghost in ghosts)
        {
            archive.ghosts[ghost.Item.SourceId] = ghost;
        }
        return archive;
    }
}
