using Pulp.Domain.Calendar;
using Pulp.Domain.Core;
using Pulp.Domain.People;
using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Calendar;

/// <summary>
/// A follow by a child, a removed viewer or a household with no roster must never reach the shared calendar:
/// it put a child's name on it, or made <see cref="CalendarItems.ForHousehold"/> throw because only adults
/// have spoiler holds. Invented household: Sam and Alex are adults, Robin is a child.
/// </summary>
public class CalendarFollowsTests
{
    private static readonly ViewerId Sam = new("sam");
    private static readonly ViewerId Alex = new("alex");
    private static readonly ViewerId Robin = new("robin");
    private static readonly ViewerId Gone = new("moved-out");
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    private static HouseholdRoster Roster() =>
        new([new HouseholdViewer(Sam, false), new HouseholdViewer(Alex, false), new HouseholdViewer(Robin, true)]);

    [Fact]
    public void OnlyAnAdultOnTheRostersFollowsSurvive()
    {
        var follows = new SportsFollow[] { new(Sam, Gulls.Id), new(Robin, Gulls.Id), new(Gone, Herons.Id), new(Alex, Herons.Id) };

        var kept = CalendarFollows.OfAdults(follows, Roster());

        Assert.Equal([new SportsFollow(Sam, Gulls.Id), new SportsFollow(Alex, Herons.Id)], kept);
    }

    [Fact]
    public void WithNoRosterNoFollowSurvives()
    {
        Assert.Empty(CalendarFollows.OfAdults([new SportsFollow(Sam, Gulls.Id)], null));
    }

    [Fact]
    public void OnlyAChildOnTheRosterIsRefusedAFollow()
    {
        Assert.True(CalendarFollows.RefusesFollow(Roster(), Robin));
        Assert.False(CalendarFollows.RefusesFollow(Roster(), Sam));
        Assert.False(CalendarFollows.RefusesFollow(null, Robin));
    }

    [Fact]
    public void AStrayFollowDoesNotBreakTheHouseholdCalendarOrNameTheChild()
    {
        var roster = Roster();
        var adults = roster.Viewers.Where(viewer => !viewer.IsChild).Select(viewer => viewer.Id).ToList();
        var stray = new SportsFollow[] { new(Sam, Gulls.Id), new(Robin, Gulls.Id), new(Gone, Gulls.Id) };
        var feeds = new[]
        {
            CoastalFeed(SeasonPhase.RegularSeason) with
            {
                Schedule = [new Game(new GameId("g-regular"), Foxes, Gulls, Now.AddDays(3), GameStage.RegularSeason, "Gridline TV · channel 7")],
                Championship = null,
            },
        };

        // The bug as it was: games from every follow, holds only for adults.
        var unfilteredHeld = CalendarScreenHolds.ByAdult(adults, feeds, stray, _ => [], Now);
        var unfilteredGames = SportsFollowing.Fixtures(feeds, stray, Now);
        Assert.Throws<ArgumentException>(() =>
            CalendarItems.ForHousehold(new CalendarSources([], [], unfilteredGames, []), DateOnly.FromDateTime(Now.DateTime), Zone, unfilteredHeld));

        // The fix: the same read, through the filter.
        var follows = CalendarFollows.OfAdults(stray, roster);
        var held = CalendarScreenHolds.ByAdult(adults, feeds, follows, _ => [], Now);
        var games = SportsFollowing.Fixtures(feeds, follows, Now);
        var items = CalendarItems.ForHousehold(new CalendarSources([], [], games, []), DateOnly.FromDateTime(Now.DateTime), Zone, held);

        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.DoesNotContain(Robin, item.Whose));
        Assert.All(items, item => Assert.DoesNotContain(Gone, item.Whose));
    }
}
