using Android.Content;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Dash;

namespace Post;

/// <summary>
/// What Post holds when the wrist asks or acts (P14 B2), read from the stores the Mailbox reads (<c>ReadPageAndBuild</c>):
/// the live tail and the first cache page (<see cref="MailboxSources"/>), what is hidden, spam, the process's delete buffer
/// (<see cref="SourceDeletes.Held"/>), the tombstones, the identity links, the read stamps, the group names and the
/// address book's names. The relay is built while the database is open, since the Mailbox's view is made as it is built.
/// Off the main thread: every read is a provider or database query.
/// </summary>
internal static class DashSnapshots
{
    public static DashRelay Relay(Context context)
    {
        var items = MailboxSources.LiveTail(context);
        using var database = TimelineDatabase.Open(context);
        items.AddRange(database.Cache.LoadPage(null, MailboxSources.CachePageSize));

        return new DashRelay(new DashSnapshot(
            items,
            database.Hidden.LoadHidden(),
            SpamStore.Load(context),
            SourceDeletes.Held,
            database.Filing.LoadTombstones(),
            ContactAssociations.BuildResolver(context, database.IdentityLinks),
            new ReadState(database.ReadState.LoadAll()),
            GroupNameStore.Load(context),
            key => ContactNameResolver.DisplayName(context, key)));
    }
}
