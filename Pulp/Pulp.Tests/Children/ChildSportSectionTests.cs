using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Ratings;
using Pulp.Domain.Sports;

namespace Pulp.Tests.Children;

/// <summary>
/// The sport a child sees inside Watching (D231 amended by D325): default-deny, the intersection for a group,
/// the hold kept with no peek, rows of title, day, time and channel and nothing else, and Play only where the
/// one hand-off door would let the children start.
/// </summary>
public class ChildSportSectionTests
{
    private static readonly LeagueId Harbor = new("harbor-league");
    private static readonly LeagueId Ridge = new("ridge-league");
    private static readonly ViewerId Parent = new("parent-example-a");
    private static readonly ViewerId Pip = new("pip-example");
    private static readonly ServiceId Peacock = new("peacock");
    private static readonly DateTimeOffset Now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tomorrow = new(2026, 5, 3, 19, 30, 0, TimeSpan.Zero);

    private static Team TeamOf(string id) => new(new TeamId(id), id + " team", "art");

    private static Game GameOf(string id, GameStage stage, DateTimeOffset at, string channel = "Gridline TV") =>
        new(new GameId(id), TeamOf("gulls"), TeamOf("herons"), at, stage, channel);

    private static LeagueFeed Feed(LeagueId id, params Game[] games)
    {
        var gulls = TeamOf("gulls");
        var herons = TeamOf("herons");
        var league = new League(id, id.Value, "art", SeasonPhase.RegularSeason, new DateOnly(2026, 9, 1), null);
        return new LeagueFeed(league, [gulls, herons], games, [gulls, herons], [new Highlight("vid1", "They won it all")], null);
    }

    private static readonly IReadOnlyCollection<SportsFollow> Follows = [new SportsFollow(Parent, new TeamId("gulls"))];

    private static ChildSportContext Context(IEnumerable<ServiceId>? ticked = null, IEnumerable<ServiceId>? attested = null, ChildRules? rules = null) =>
        new([rules ?? new ChildRules(Pip, AgeBand.SevenToNine)], [.. ticked ?? []], new AttestedServices(attested ?? []), TimeZoneInfo.Utc);

    private static ChildSportSection? Build(
        IReadOnlyCollection<LeagueFeed> feeds,
        IReadOnlySet<LeagueId> allowed,
        ChildSportContext? context = null,
        IReadOnlyList<WatchEvent>? parentSittings = null,
        IReadOnlyCollection<SportsFollow>? follows = null) =>
        ChildSportSection.Build(feeds, allowed, follows ?? Follows, _ => parentSittings ?? [], context ?? Context(), Now);

    [Fact]
    public void NothingAllowedIsNoSectionAtAll()
    {
        var feeds = new[] { Feed(Harbor, GameOf("g1", GameStage.RegularSeason, Tomorrow)) };

        Assert.Null(Build(feeds, new HashSet<LeagueId>()));
    }

    [Fact]
    public void ALeagueNotAllowedIsAbsentWhileAnAllowedOneShows()
    {
        var feeds = new[]
        {
            Feed(Harbor, GameOf("g1", GameStage.RegularSeason, Tomorrow)),
            Feed(Ridge, GameOf("g2", GameStage.RegularSeason, Tomorrow)),
        };

        var section = Build(feeds, new HashSet<LeagueId> { Harbor });

        Assert.Equal([Harbor], section!.Leagues.Select(league => league.Id));
    }

    [Fact]
    public void AllowedButNobodyFollowsAnythingThereIsNoSection()
    {
        var feeds = new[] { Feed(Harbor, GameOf("g1", GameStage.RegularSeason, Tomorrow)) };

        Assert.Null(Build(feeds, new HashSet<LeagueId> { Harbor }, follows: []));
    }

    [Fact]
    public void ALeagueWithNothingToShowLeavesNoEmptyCard()
    {
        var feeds = new[] { Feed(Harbor) };

        Assert.Null(Build(feeds, new HashSet<LeagueId> { Harbor }));
    }

    [Fact]
    public void AGameRowIsTitleDayTimeAndChannelOnly()
    {
        var feeds = new[] { Feed(Harbor, GameOf("g1", GameStage.RegularSeason, Tomorrow)) };

        var game = Build(feeds, new HashSet<LeagueId> { Harbor })!.Leagues.Single().Games.Single();

        Assert.Equal("herons team at gulls team", game.Title);
        Assert.Equal("Tomorrow", game.Day);
        Assert.Equal("7:30 pm", game.Time);
        Assert.Equal("On Gridline TV", game.ChannelLine);
        Assert.Equal(
            ["ChannelLine", "Day", "Id", "Play", "Time", "Title"],
            typeof(ChildSportGame).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ASectionCarriesNoStandingNoHighlightNoScore()
    {
        var names = new[] { typeof(ChildSportSection), typeof(ChildSportLeague), typeof(ChildSportGame) }
            .SelectMany(type => type.GetProperties().Select(property => property.Name))
            .ToList();

        Assert.DoesNotContain(names, name => name.Contains("Standing", StringComparison.Ordinal)
            || name.Contains("Highlight", StringComparison.Ordinal)
            || name.Contains("Score", StringComparison.Ordinal)
            || name.Contains("Record", StringComparison.Ordinal)
            || name.Contains("Peek", StringComparison.Ordinal));
    }

    [Fact]
    public void AHeldLeagueKeepsItsHoldWithNoRevealingFixtureAndNoPeek()
    {
        var started = new DateTimeOffset(2026, 5, 1, 19, 0, 0, TimeSpan.Zero);
        var feeds = new[]
        {
            Feed(
                Harbor,
                GameOf("past", GameStage.RegularSeason, started),
                GameOf("next", GameStage.Playoffs, Tomorrow)),
        };

        var section = Build(feeds, new HashSet<LeagueId> { Harbor });
        var held = Build(feeds, new HashSet<LeagueId> { Harbor }, parentSittings: [])!.Leagues.Single();

        // The unwatched recording holds the league: the playoff fixture, which would say who won, is absent.
        Assert.True(held.Held);
        Assert.DoesNotContain(held.Games, game => game.Id == new GameId("next"));
        Assert.Contains(held.Games, game => game.Id == new GameId("past"));
        Assert.NotNull(section);
    }

    [Fact]
    public void ARecordingThatWasWatchedLiftsTheHoldAndShowsTheNextGame()
    {
        var started = new DateTimeOffset(2026, 5, 1, 19, 0, 0, TimeSpan.Zero);
        var feeds = new[]
        {
            Feed(
                Harbor,
                GameOf("past", GameStage.RegularSeason, started),
                GameOf("next", GameStage.Playoffs, Tomorrow)),
        };
        var watched = new WatchEvent(new TitleId("past"), WatchedKind.Game, started.AddHours(3), null, Evidence: WatchSource.MarkedWatched);

        var league = Build(feeds, new HashSet<LeagueId> { Harbor }, parentSittings: [watched])!.Leagues.Single();

        Assert.False(league.Held);
        Assert.Equal([new GameId("next")], league.Games.Select(game => game.Id));
    }

    [Fact]
    public void GamesAcrossFollowedTeamsAreOneRowEachInTimeOrder()
    {
        var later = Tomorrow.AddDays(1);
        var feeds = new[]
        {
            Feed(Harbor, GameOf("b", GameStage.RegularSeason, later), GameOf("a", GameStage.RegularSeason, Tomorrow)),
        };
        var both = new[] { new SportsFollow(Parent, new TeamId("gulls")), new SportsFollow(Parent, new TeamId("herons")) };

        var games = Build(feeds, new HashSet<LeagueId> { Harbor }, follows: both)!.Leagues.Single().Games;

        // Both teams play the same next game; a game is one row however many followed teams are in it.
        Assert.Equal([new GameId("a")], games.Select(game => game.Id));
    }

    [Fact]
    public void NoPlayWhenTheServiceIsNotTicked()
    {
        var feeds = new[] { Feed(Harbor, GameOf("g1", GameStage.RegularSeason, Tomorrow, "Peacock")) };

        var game = Build(feeds, new HashSet<LeagueId> { Harbor }, Context(attested: [Peacock]))!.Leagues.Single().Games.Single();

        Assert.Null(game.Play);
    }

    [Fact]
    public void NoPlayWhenTheServiceIsNotAttestedForChildren()
    {
        var feeds = new[] { Feed(Harbor, GameOf("g1", GameStage.RegularSeason, Tomorrow, "Peacock")) };

        var game = Build(feeds, new HashSet<LeagueId> { Harbor }, Context(ticked: [Peacock]))!.Leagues.Single().Games.Single();

        Assert.Null(game.Play);
    }

    [Fact]
    public void PlayIsOfferedForAnAllowedLeaguesUnratedGameOnAnAttestedService()
    {
        var feeds = new[] { Feed(Harbor, GameOf("g1", GameStage.RegularSeason, Tomorrow, "Peacock")) };

        var game = Build(feeds, new HashSet<LeagueId> { Harbor }, Context(ticked: [Peacock], attested: [Peacock]))!.Leagues.Single().Games.Single();

        Assert.True(game.Play!.Opens);
        Assert.Equal(Peacock, game.Play.Service);
    }

    [Fact]
    public void NoPlayForAnAllowedLeaguesGameOnAServiceNotAttestedForChildren()
    {
        var feeds = new[] { Feed(Harbor, GameOf("g1", GameStage.RegularSeason, Tomorrow, "Peacock")) };

        var game = Build(feeds, new HashSet<LeagueId> { Harbor }, Context(ticked: [Peacock]))!.Leagues.Single().Games.Single();

        Assert.Null(game.Play);
    }

    [Fact]
    public void AGroupGetsPlayOnlyWhenEveryChildHasTheLeagueAllowed()
    {
        var feeds = new[] { Feed(Harbor, GameOf("g1", GameStage.RegularSeason, Tomorrow, "Peacock")) };
        var pip = new ChildRules(Pip, AgeBand.SevenToNine);
        var moss = new ChildRules(new ViewerId("moss-example"), AgeBand.SevenToNine);
        var group = new ChildSportContext([pip, moss], [Peacock], new AttestedServices([Peacock]), TimeZoneInfo.Utc);

        // The section's allowed set is the intersection; with a league outside it nothing is listed, and so nothing plays.
        Assert.Null(Build(feeds, new HashSet<LeagueId>(), group));
        var game = Build(feeds, new HashSet<LeagueId> { Harbor }, group)!.Leagues.Single().Games.Single();
        Assert.True(game.Play!.Opens);
    }

    [Fact]
    public void AHeldLeaguesRevealingGameIsStillAbsentAndTheOldOneStillPlays()
    {
        var started = new DateTimeOffset(2026, 5, 1, 19, 0, 0, TimeSpan.Zero);
        var feeds = new[]
        {
            Feed(
                Harbor,
                GameOf("past", GameStage.RegularSeason, started, "Peacock"),
                GameOf("next", GameStage.Playoffs, Tomorrow, "Peacock")),
        };

        var league = Build(feeds, new HashSet<LeagueId> { Harbor }, Context(ticked: [Peacock], attested: [Peacock]))!.Leagues.Single();

        Assert.True(league.Held);
        Assert.DoesNotContain(league.Games, game => game.Id == new GameId("next"));
    }

    [Fact]
    public void ABlockedTitleBeatsTheLeagueAllowance()
    {
        var feeds = new[] { Feed(Harbor, GameOf("g1", GameStage.RegularSeason, Tomorrow, "Peacock")) };
        var rules = new ChildRules(Pip, AgeBand.SevenToNine) { BlockedTitles = new HashSet<TitleId> { new("g1") } };

        var game = Build(feeds, new HashSet<LeagueId> { Harbor }, Context(ticked: [Peacock], attested: [Peacock], rules: rules))!
            .Leagues.Single().Games.Single();

        Assert.Null(game.Play);
    }

    [Fact]
    public void PlayOpensTheServicesHomeWhenTheGateWouldLetEveryChildStart()
    {
        var feeds = new[] { Feed(Harbor, GameOf("g1", GameStage.RegularSeason, Tomorrow, "Peacock")) };
        var rules = new ChildRules(Pip, AgeBand.SevenToNine) { Allowed = new HashSet<TitleId> { new("g1") } };

        var game = Build(feeds, new HashSet<LeagueId> { Harbor }, Context(ticked: [Peacock], attested: [Peacock], rules: rules))!
            .Leagues.Single().Games.Single();

        Assert.True(game.Play!.Opens);
        Assert.Equal(Peacock, game.Play.Service);
    }

    [Fact]
    public void ABlockedServiceIsNoPlayEvenForAnAllowedGame()
    {
        var feeds = new[] { Feed(Harbor, GameOf("g1", GameStage.RegularSeason, Tomorrow, "Peacock")) };
        var rules = new ChildRules(Pip, AgeBand.SevenToNine)
        {
            Allowed = new HashSet<TitleId> { new("g1") },
            BlockedServices = new HashSet<ServiceId> { Peacock },
        };

        var game = Build(feeds, new HashSet<LeagueId> { Harbor }, Context(ticked: [Peacock], attested: [Peacock], rules: rules))!
            .Leagues.Single().Games.Single();

        Assert.Null(game.Play);
    }
}
