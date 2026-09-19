using Android.Content;
using Codex.Domain.Post;
using Codex.Domain.Timeline;

namespace Post;

/// <summary>
/// Filing, carried out. The rules are pure and tested in <see cref="Tombstones"/> and
/// <see cref="GhostArchive"/>; this is the part that touches the disk and the source.
///
/// The order matters and is the whole point: the ghost is written and the tombstone raised
/// FIRST, so the item is out of the Mailbox and safe in the archive before anything is
/// asked of the network. A filing must never depend on a delete landing — on a phone the
/// delete often will not land for a while, and an owner watching a message sit there while
/// the network is consulted concludes filing does not work.
/// </summary>
internal static class Filing
{
    /// <summary>
    /// Files an item: ghost kept, tombstone raised, item out of the live cache. Returns
    /// immediately; the source delete is owed and retried by the sync.
    /// </summary>
    public static void File(Context context, TimelineItem item, DateTimeOffset now)
    {
        using var database = TimelineDatabase.Open(context);

        database.Filing.SaveGhost(new Ghost(item, now));

        var tombstones = database.Filing.LoadTombstones();
        tombstones.Mark(item.SourceId, now);
        database.Filing.SaveTombstones(tombstones);

        database.Cache.Delete(item.Kind, item.SourceId);

        // Calls and texts are read LIVE from their provider on every Mailbox reload rather than
        // mirrored into the SQL cache the way mail is, so deleting from Cache alone did nothing
        // for them — the very next reload read the row straight back out of the provider and the
        // correspondence never actually left the Mailbox. Hidden is the same suppression
        // MailboxArea already applies for a permanent per-message delete (list and open
        // correspondence alike); applying it here too is what makes a filed call/text
        // actually disappear until PostSync's retry loop (see PostSync.DeleteAtSourceAsync)
        // deletes it at the source for real.
        if (item.Kind != TimelineItemKind.Email)
        {
            database.Hidden.Hide(item.Kind, item.SourceId);
        }
    }

    /// <summary>
    /// Takes something back out of Filed. What returns is the ghost — the source original
    /// is already gone, or is on its way to being — so it goes back into the live cache as
    /// itself, and its tombstone is withdrawn so reconcile stops suppressing it.
    /// </summary>
    public static TimelineItem? Unfile(Context context, string sourceId)
    {
        using var database = TimelineDatabase.Open(context);

        var archive = database.Filing.LoadArchive();
        var item = archive.Unfile(sourceId);
        if (item is null)
        {
            return null;
        }

        database.Filing.RemoveGhost(sourceId);

        var tombstones = database.Filing.LoadTombstones();
        tombstones.SourceConfirmed(sourceId);
        database.Filing.SaveTombstones(tombstones);

        database.Cache.Upsert([item]);
        database.Hidden.Unhide(item.Kind, sourceId);
        return item;
    }
}
