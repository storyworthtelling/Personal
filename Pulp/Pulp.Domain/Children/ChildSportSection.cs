using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Services;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Children;

/// <summary>
/// What a child's sport section needs beyond the feeds (D325): the children watching, the services the
/// household ticked, which of those are attested for children, and the zone their clocks are read in.
/// </summary>
public sealed record ChildSportContext(
    IReadOnlyList<ChildRules> Children,
    IReadOnlyCollection<ServiceId> Ticked,
    AttestedServices Attested,
    TimeZoneInfo Zone);

/// <summary>One row in the child's sport section: the game's title, its day, its time and its channel, and Play only when the gate would let the children start.</summary>
public sealed record ChildSportGame(GameId Id, string Title, string Day, string Time, string? ChannelLine, GamePlayDecision? Play);

/// <summary>One allowed league in the section. <paramref name="Held"/> only says the hold is on; nothing a hold withholds is carried, and a child cannot peek.</summary>
public sealed record ChildSportLeague(LeagueId Id, string Name, bool Held, IReadOnlyList<ChildSportGame> Games);

/// <summary>
/// The "Sport" section below a child's shows in Watching (D231 as amended by D325). Default-deny: it exists only
/// when a league is allowed for every child watching (<see cref="ChildSports.AllowedForAll"/>), an adult follows
/// a team in it, and there is a game to list; otherwise there is no section and no empty card. Built from
/// <see cref="ChildSports.Present"/>, so a held league stays held (with no peek) and nothing about a result, a
/// standing or a highlight has a field here.
/// </summary>
public sealed record ChildSportSection(string Heading, IReadOnlyList<ChildSportLeague> Leagues)
{
    public const string Title = "Sport";

    /// <param name="feeds">Whatever feeds are in hand; only allowed leagues are read.</param>
    /// <param name="allowed">The leagues every child watching may see.</param>
    /// <param name="follows">The adults' follows (D60); a child has none of their own, so these are the teams shown.</param>
    /// <param name="sittingsOf">An adult's watch events, which say which recordings are still unwatched and so which leagues are held.</param>
    public static ChildSportSection? Build(
        IEnumerable<LeagueFeed> feeds,
        IReadOnlySet<LeagueId> allowed,
        IReadOnlyCollection<SportsFollow> follows,
        Func<ViewerId, IReadOnlyList<WatchEvent>> sittingsOf,
        ChildSportContext context,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(feeds);
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(follows);
        ArgumentNullException.ThrowIfNull(sittingsOf);
        ArgumentNullException.ThrowIfNull(context);
        if (allowed.Count == 0 || follows.Count == 0)
        {
            return null;
        }

        var teams = follows.Select(follow => follow.Team).Distinct().ToList();
        var adults = follows.Select(follow => follow.Viewer).Distinct().ToList();
        var leagues = new List<ChildSportLeague>();
        var readable = feeds.Where(feed => allowed.Contains(feed.League.Id)).DistinctBy(feed => feed.League.Id).ToDictionary(feed => feed.League.Id);
        foreach (var feed in LeagueOrder.Order(readable.Values.Select(feed => feed.League)).Select(league => readable[league.Id]))
        {
            // A league is held if any adult's team there has a recording that adult has not watched: the strictest reading.
            var recordings = adults
                .SelectMany(adult => RecordedGames.Unwatched(
                    feed, follows.Where(follow => follow.Viewer == adult).Select(follow => follow.Team).ToList(), sittingsOf(adult), now))
                .Distinct()
                .ToList();
            if (ChildSports.Present(feed, allowed, teams, recordings, now) is not { } sheet)
            {
                continue;
            }

            var (line, teamGames) = sheet switch
            {
                HeldLeague held => (held.Line, held.Teams),
                OpenLeague open => (open.Line, open.Teams),
                _ => throw new ArgumentOutOfRangeException(nameof(sheet), sheet, "A league sheet that is neither held nor open."),
            };

            // A championship is its name and date and nothing of who is in it (D239), held or not.
            var championship = sheet switch
            {
                HeldLeague { Championship: { } held } => (Name: held.Name, At: held.StartsAt),
                OpenLeague { Championship: { } open } => (Name: open.Name, At: open.StartsAt),
                _ => ((string Name, DateTimeOffset At)?)null,
            };

            var rows = teamGames
                .SelectMany(entry => entry.Games)
                .DistinctBy(game => game.Id)
                .Select(game => new ChildSportGame(
                    game.Id,
                    game.Away.Name + " at " + game.Home.Name,
                    SportsScreenText.DayLabel(game.StartsAt, now, context.Zone),
                    SportsScreenText.Clock12(game.StartsAt, context.Zone),
                    SportsScreenText.ChannelLine(game.Channel),
                    PlayFor(game, feed.League.Id, allowed, context)))
                .ToList();
            if (championship is { } named)
            {
                rows.Add(new ChildSportGame(
                    new GameId(line.Id.Value + "-championship"),
                    named.Name,
                    SportsScreenText.DayLabel(named.At, now, context.Zone),
                    SportsScreenText.Clock12(named.At, context.Zone),
                    null,
                    null));
            }

            if (rows.Count > 0)
            {
                leagues.Add(new ChildSportLeague(line.Id, line.Name, sheet is HeldLeague, rows));
            }
        }

        return leagues.Count == 0 ? null : new ChildSportSection(Title, leagues);
    }

    /// <summary>
    /// Play for a game, or null. The channel must resolve to a ticked service (<see cref="GamePlay.Decide"/>), and the
    /// one door's own question must come back yes for every child: <see cref="ChildFilter.CanStart"/> on the very
    /// facts <see cref="GamePlay.Request"/> hands the planner, so an unrated game, a blocked service or a service
    /// not attested for children (G13) offers nothing rather than a Play the gate would refuse. A game has no rating
    /// (and none is invented): a league in <paramref name="allowed"/> (every child's allowance, D317) is the parents'
    /// explicit allow for its games (D326), so each child is judged with that league allowed, and a block still wins.
    /// </summary>
    private static GamePlayDecision? PlayFor(GameLine game, LeagueId league, IReadOnlySet<LeagueId> allowed, ChildSportContext context)
    {
        var decision = GamePlay.Decide(game.Channel, context.Ticked, BroadcasterTable.Public, ServiceCatalogue.Public);
        if (!decision.Opens || ServiceCatalogue.Public.ById(decision.Service!.Value) is not { } service)
        {
            return null;
        }

        var facts = GamePlay.Request(context.Children[0].Child, game.Id, service, decision.Home!, game.Away.Name + " at " + game.Home.Name, league).Title;
        var children = context.Children.Select(child => child with { AllowedLeagues = child.AllowedLeagues.Union(allowed).ToHashSet() }).ToList();
        return ChildFilter.CanStart(children, facts, service.Id, ServiceCatalogue.Public, context.Attested).MayStart ? decision : null;
    }
}
