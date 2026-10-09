using Pulp.Domain.Core;
using Pulp.Domain.Library;

namespace Pulp.Domain.Catalogue;

/// <summary>
/// A show's catalogue entry as the <see cref="ShowSchedule"/> Continue Watching already reads
/// (D96, D152): which episode is next is that rule's job, not a second one here. Only dated episodes
/// are scheduled — an episode with no date cannot be said to have come out — and a date with no
/// time reads as the start of that day in UTC.
/// </summary>
public static class CatalogueSchedule
{
    public static ShowSchedule For(CatalogueTitle show)
    {
        ArgumentNullException.ThrowIfNull(show);
        if (show.Kind != TitleKind.Show)
        {
            throw new ArgumentException($"'{show.Id}' is a film; only a show has a schedule.", nameof(show));
        }

        var episodes = show.Seasons
            .SelectMany(season => season.Episodes)
            .Select(episode => (episode.Episode, When: episode.AirsAt ?? StartOfDay(episode.AirDate)))
            .Where(episode => episode.When is not null)
            .Select(episode => new ScheduledEpisode(episode.Episode, episode.When!.Value))
            .OrderBy(episode => episode.Episode)
            .ToList();

        return new ShowSchedule(show.Id, episodes, show.Ended ?? false);
    }

    private static DateTimeOffset? StartOfDay(DateOnly? date) =>
        date is null ? null : new DateTimeOffset(date.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
