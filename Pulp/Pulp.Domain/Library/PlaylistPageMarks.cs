using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;

namespace Pulp.Domain.Library;

/// <summary>
/// What "Mark watched" on a playlist row writes (D160, D278): marks by hand that
/// <see cref="PlaylistPageStanding"/> reads back as watched, so the row strikes (D101). A film (or a
/// game) takes one whole-title mark; a show takes one per episode that has aired, since a show's
/// standing is counted by episode — a single whole-show mark would leave its row unstruck. A show
/// whose episodes are not known, or a franchise, gets no marks rather than one nothing reads.
/// </summary>
public static class PlaylistPageMarks
{
    public static IReadOnlyList<WatchEvent> Watch(PlaylistEntry entry, CatalogueTitle? title, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var id = new TitleId(entry.Key);

        switch (entry.Kind)
        {
            case PlaylistEntryKind.Film:
                return [new WatchEvent(id, WatchedKind.Film, at, Service: null, Evidence: WatchSource.MarkedWatched)];
            case PlaylistEntryKind.Game:
                return [new WatchEvent(id, WatchedKind.Game, at, Service: null, Evidence: WatchSource.MarkedWatched)];
            case PlaylistEntryKind.Franchise:
                return [];
            case PlaylistEntryKind.Show:
                if (title is not { Kind: TitleKind.Show } show)
                {
                    return [];
                }

                return CatalogueSchedule.For(show).Episodes
                    .Where(episode => episode.AirsAt <= at)
                    .Select(episode => new WatchEvent(id, WatchedKind.Show, at, Service: null, episode.Episode, WatchSource.MarkedWatched))
                    .ToList();
            default:
                throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, "A playlist entry kind with no mark decided.");
        }
    }
}
