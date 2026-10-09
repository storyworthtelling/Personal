using Pulp.Domain.Core;

namespace Pulp.Domain.Sports;

/// <summary>
/// An upcoming followed game or championship, with every adult who follows it (D60) — what the
/// Calendar and the household Google calendar are built from.
/// </summary>
public sealed record Fixture(
    GameId Id,
    LeagueId League,
    string Name,
    DateTimeOffset StartsAt,
    GameStage Stage,
    string Channel,
    IReadOnlyList<ViewerId> Followers);

/// <summary>
/// Follows are per adult (D60); adding a team brings its whole league in (D67), so a league is
/// followed when any of its teams is. A followed league's championship is always followed, whether
/// or not a followed team is in it (D239) — and only a followed league's (D240).
/// </summary>
public static class SportsFollowing
{
    public static IReadOnlyList<LeagueId> FollowedLeagues(
        IEnumerable<LeagueFeed> feeds, IEnumerable<SportsFollow> follows, ViewerId viewer)
    {
        ArgumentNullException.ThrowIfNull(feeds);
        ArgumentNullException.ThrowIfNull(follows);
        var teams = follows.Where(follow => follow.Viewer == viewer).Select(follow => follow.Team).ToHashSet();
        return feeds
            .Where(feed => feed.Teams.Any(team => teams.Contains(team.Id)))
            .Select(feed => feed.League.Id)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Every game after <paramref name="now"/> that some adult follows, plus each followed league's
    /// championship, labelled with its followers. A championship's followers are everyone following
    /// any team in that league.
    /// </summary>
    public static IReadOnlyList<Fixture> Fixtures(
        IEnumerable<LeagueFeed> feeds, IReadOnlyCollection<SportsFollow> follows, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(feeds);
        ArgumentNullException.ThrowIfNull(follows);
        var fixtures = new List<Fixture>();

        foreach (var feed in feeds)
        {
            var members = feed.Teams.Select(team => team.Id).ToHashSet();
            var leagueFollowers = Viewers(follows.Where(follow => members.Contains(follow.Team)));
            if (leagueFollowers.Count == 0)
            {
                continue;
            }

            // The championship is added once below, even when the feed also lists it on the schedule.
            foreach (var game in feed.Schedule.Where(game => game.StartsAt > now && game.Id != feed.Championship?.Id))
            {
                var followers = Viewers(follows.Where(follow => game.Involves(follow.Team)));
                if (followers.Count > 0)
                {
                    fixtures.Add(new Fixture(
                        game.Id, feed.League.Id, game.Name, game.StartsAt, game.Stage, game.Channel, followers));
                }
            }

            var championship = feed.Championship;
            if (championship is not null && championship.StartsAt > now)
            {
                fixtures.Add(new Fixture(
                    championship.Id,
                    feed.League.Id,
                    championship.Name,
                    championship.StartsAt,
                    GameStage.Championship,
                    string.Empty,
                    leagueFollowers));
            }
        }

        return fixtures;
    }

    private static List<ViewerId> Viewers(IEnumerable<SportsFollow> follows) =>
        follows.Select(follow => follow.Viewer).Distinct().OrderBy(viewer => viewer.Value, StringComparer.OrdinalIgnoreCase).ToList();
}
