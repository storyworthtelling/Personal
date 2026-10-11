using Pulp.Domain.Calendar;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Calendar;

/// <summary>
/// The nightly writer is handed each adult's own spoiler holds (D70, D242): <c>ForHousehold</c> throws for an
/// adult who follows a game and has no entry, so an adult who holds nothing must still be there, with an
/// empty set — and an adult who is holding must be held for, or the shared calendar spoils them.
/// </summary>
public class CalendarScreenHoldsTests
{
    private static readonly ViewerId Sam = new("sam");
    private static readonly ViewerId Alex = new("alex");

    private static WatchEvent Watched(GameId game) =>
        new(new TitleId(game.Value), WatchedKind.Game, Now.AddDays(-1), null, Evidence: WatchSource.MarkedWatched);

    private static IEnumerable<WatchEvent> Nothing(ViewerId viewer) => [];

    [Fact]
    public void AnAdultWithAnUnwatchedRecordingHoldsTheLeagueAndTheOtherDoesNot()
    {
        // Sam follows the Gulls and has not seen the Foxes game; Alex follows the Herons and saw theirs.
        SportsFollow[] follows = [new(Sam, Gulls.Id), new(Alex, Herons.Id)];

        var held = CalendarScreenHolds.ByAdult(
            [Sam, Alex], [CoastalFeed()], follows,
            viewer => viewer == Alex ? [Watched(HeronsWatched)] : [],
            Now);

        Assert.Equal([Coastal], held[Sam]);
        Assert.Empty(held[Alex]);
    }

    [Fact]
    public void AnAdultWhoFollowsNothingStillHasAnEntryWithAnEmptySet()
    {
        var held = CalendarScreenHolds.ByAdult([Sam, Alex], [CoastalFeed()], [new SportsFollow(Sam, Gulls.Id)], Nothing, Now);

        Assert.True(held.ContainsKey(Alex));
        Assert.Empty(held[Alex]);
    }

    [Fact]
    public void TheHouseholdCalendarNeverThrowsForTheseHoldsAndDropsTheHeldLeaguesPostseason()
    {
        SportsFollow[] follows = [new(Sam, Gulls.Id), new(Alex, Herons.Id)];
        var feed = CoastalFeed() with { Championship = new Championship(CoastalBowl, "The Coastal Bowl", Now.AddDays(9), [Herons, Gulls]) };
        var held = CalendarScreenHolds.ByAdult([Sam, Alex], [feed], follows, Nothing, Now);
        var fixtures = SportsFollowing.Fixtures([feed], follows, Now);
        var sources = new CalendarSources([], [], fixtures, []);
        var zone = TimeZoneInfo.Utc;

        var items = CalendarItems.ForHousehold(sources, DateOnly.FromDateTime(Now.UtcDateTime), zone, held);

        Assert.NotEmpty(fixtures);
        Assert.DoesNotContain(items, item => item.Name.Contains("Gulls", StringComparison.Ordinal));
        Assert.Contains(items, item => item.Name == "The Coastal Bowl");
    }

    [Fact]
    public void AFeedNoAdultFollowsIntoHoldsNothing()
    {
        var held = CalendarScreenHolds.ByAdult([Sam], [CoastalFeed()], [new SportsFollow(Sam, new TeamId("someone-else"))], Nothing, Now);

        Assert.Empty(held[Sam]);
    }

    [Fact]
    public void ADuplicatedFeedIsOneLeague()
    {
        var feed = CoastalFeed();
        var held = CalendarScreenHolds.ByAdult([Sam], [feed, feed], [new SportsFollow(Sam, Gulls.Id)], Nothing, Now);

        Assert.Single(held[Sam]);
    }
}
