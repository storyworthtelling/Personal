using Pulp.Domain.Catalogue;
using Pulp.Domain.Sports;

namespace Pulp.Infrastructure.Sports;

/// <summary>
/// One <see cref="ISportsSource"/> over the adapters, choosing by <see cref="SportsSourceTable"/> (D208,
/// D140): the table that About prints is the table that decides, so the two cannot disagree.
/// </summary>
public sealed class RoutedSportsSource : ISportsSource
{
    private readonly SportsSourceTable table;
    private readonly IReadOnlyDictionary<string, ISportsSource> sources;

    public RoutedSportsSource(SportsSourceTable table, IReadOnlyDictionary<string, ISportsSource> sources)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(sources);
        foreach (var info in table.Sources.Where(info => !sources.ContainsKey(info.Id)))
        {
            throw new ArgumentException($"The table names the source '{info.Id}' but no adapter was given for it.", nameof(sources));
        }

        this.table = table;
        this.sources = sources;
    }

    /// <summary>The shipped composition: MLB's own feed, ESPN's JSON and the PWHL's calendar file over the one client.</summary>
    public static RoutedSportsSource Shipped(HttpClient httpClient, ILookupCounter? counter) => new(
        SportsSourceTable.Default,
        new Dictionary<string, ISportsSource>(StringComparer.OrdinalIgnoreCase)
        {
            [SportsSourceIds.MlbStats] = new MlbStatsSource(httpClient, counter),
            [SportsSourceIds.Espn] = new EspnSource(httpClient, EspnLeagues.Default, counter),
            [SportsSourceIds.PwhlCalendar] = new PwhlCalendarSource(httpClient, counter),
        });

    public IReadOnlyList<LeagueId> Leagues => table.Leagues.Select(entry => entry.League).ToList();

    public Task<LeagueSchedule> GetScheduleAsync(LeagueId league, SportsWindow window, CancellationToken cancellationToken) =>
        SourceOf(league).GetScheduleAsync(league, window, cancellationToken);

    public Task<IReadOnlyList<Team>> GetStandingsOrderAsync(LeagueId league, CancellationToken cancellationToken) =>
        SourceOf(league).GetStandingsOrderAsync(league, cancellationToken);

    private ISportsSource SourceOf(LeagueId league) =>
        table.SourceOf(league) is { } id
            ? sources[id]
            : throw new ArgumentException($"League '{league}' is not in the sports source table, so no source is asked about it.", nameof(league));
}
