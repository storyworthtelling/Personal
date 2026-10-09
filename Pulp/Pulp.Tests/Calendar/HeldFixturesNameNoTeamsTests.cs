using Pulp.Domain.Calendar;
using Pulp.Domain.Core;
using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Calendar;

/// <summary>
/// The calendar's half of G10 (D237, D239, D242, D284). G10 proves a held league's <i>view model</i>
/// cannot carry a result; this proves the other road out — feed → <see cref="SportsFollowing.Fixtures"/>
/// → <see cref="CalendarItems"/> → the Calendar page and the household Google calendar — carries no
/// team name for a held league's revealing games either. Who is in a playoff or final says who won
/// the round before, so a held league's postseason reaches the calendar only as the championship's
/// own name and date.
///
/// Built through the real <see cref="SportsFollowing.Fixtures"/>, never a hand-made fixture: a
/// fixture written by hand with a safe name is exactly how the multi-game-final leak passed before.
/// </summary>
public class HeldFixturesNameNoTeamsTests
{
    private static readonly ViewerId Sam = new("sam");
    private static readonly ViewerId Alex = new("alex");
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.DateTime);
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Invented/Harbor", TimeSpan.FromHours(-5), "Invented Harbor", "Invented Harbor");

    private static readonly GameId FinalOne = new("final-1");
    private static readonly GameId FinalTwo = new("final-2");

    private static readonly SportsFollow[] Follows = [new(Sam, Gulls.Id), new(Alex, Herons.Id)];

    /// <summary>
    /// A postseason with a two-game final between the Gulls and the Herons — which exists only because
    /// both won their semi-finals — plus a playoff fixture. Every upcoming game reveals something.
    /// </summary>
    private static LeagueFeed Postseason(Championship? championship) => CoastalFeed() with
    {
        Schedule =
        [
            new Game(GullsRecording, Foxes, Gulls, Now.AddDays(-2), GameStage.Playoffs, "Gridline TV · channel 7"),
            new Game(GullsNextPlayoff, Herons, Gulls, Now.AddDays(2), GameStage.Playoffs, "Gridline TV · channel 7"),
            new Game(FinalOne, Herons, Gulls, Now.AddDays(6), GameStage.Championship, "Gridline TV · channel 7"),
            new Game(FinalTwo, Gulls, Herons, Now.AddDays(9), GameStage.Championship, "Gridline TV · channel 7"),
        ],
        Championship = championship,
    };

    private static Championship Bowl(GameId id) => new(id, "The Coastal Bowl", Now.AddDays(6), [Herons, Gulls]);

    public static TheoryData<string> Shapes() => new() { "series-final-one", "series-elsewhere", "no-championship" };

    private static LeagueFeed Shape(string shape) => shape switch
    {
        "series-final-one" => Postseason(Bowl(FinalOne)),
        "series-elsewhere" => Postseason(Bowl(CoastalBowl)),
        "no-championship" => Postseason(null),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown feed shape."),
    };

    private static IEnumerable<IReadOnlyList<CalendarItem>> EveryReading(LeagueFeed feed)
    {
        var fixtures = SportsFollowing.Fixtures([feed], Follows, Now);

        // Not vacuous: the feed must actually have handed the calendar revealing games to hold.
        Assert.Contains(fixtures, fixture => fixture.Stage == GameStage.Championship);
        Assert.Contains(fixtures, fixture => fixture.Stage == GameStage.Playoffs);

        var sources = new CalendarSources([], [], fixtures, []);
        var held = new HashSet<LeagueId> { Coastal };
        yield return CalendarItems.ForViewer(sources, Sam, Today, Zone, held);
        yield return CalendarItems.ForViewer(sources, Alex, Today, Zone, held);
        yield return CalendarItems.ForHousehold(
            sources, Today, Zone, new Dictionary<ViewerId, IReadOnlySet<LeagueId>> { [Sam] = held, [Alex] = held });
    }

    private static IEnumerable<string> Strings(CalendarItem item) =>
        new[] { item.Key, item.Name, item.Detail ?? string.Empty }.Concat(item.Playlists);

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AHeldLeaguesCalendarItemsNameNoTeam(string shape)
    {
        var feed = Shape(shape);
        var teamNames = feed.Teams.Select(team => team.Name).ToList();
        Assert.NotEmpty(teamNames);

        foreach (var items in EveryReading(feed))
        {
            var offences = items
                .Where(item => item.League == Coastal)
                .SelectMany(item => Strings(item).Select(text => (item.Key, text)))
                .Where(pair => teamNames.Any(team => pair.text.Contains(team, StringComparison.OrdinalIgnoreCase)))
                .Select(pair => pair.Key + " → \"" + pair.text + "\"")
                .ToList();

            Assert.True(offences.Count == 0,
                "A held league's calendar line names a team, which says who won the round before:\n"
                + string.Join("\n", offences));
        }
    }

    [Theory]
    [InlineData("series-final-one")]
    [InlineData("series-elsewhere")]
    public void AHeldLeaguesChampionshipKeepsItsOwnNameAndDate(string shape)
    {
        foreach (var items in EveryReading(Shape(shape)))
        {
            var bowl = Assert.Single(items, item => item.League == Coastal);
            Assert.Equal("The Coastal Bowl", bowl.Name);
            Assert.Equal(CalendarWhen.At(Now.AddDays(6), Zone), bowl.When);
        }
    }

    [Fact]
    public void WithNoChampionshipInTheFeedAHeldLeaguesPostseasonIsAbsent()
    {
        foreach (var items in EveryReading(Shape("no-championship")))
        {
            Assert.DoesNotContain(items, item => item.League == Coastal);
        }
    }

    [Fact]
    public void AnOpenLeaguesSeriesStillShowsEveryGame()
    {
        var fixtures = SportsFollowing.Fixtures([Shape("series-final-one")], Follows, Now);
        var sources = new CalendarSources([], [], fixtures, []);

        var keys = CalendarItems.ForViewer(sources, Sam, Today, Zone, new HashSet<LeagueId>())
            .Select(item => item.Key)
            .ToList();

        Assert.Equal(["game:" + GullsNextPlayoff.Value, "game:final-1", "game:final-2"], keys);
    }
}
