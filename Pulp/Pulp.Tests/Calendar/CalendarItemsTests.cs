using Pulp.Domain.Calendar;
using Pulp.Domain.Core;
using Pulp.Domain.Sports;

namespace Pulp.Tests.Calendar;

/// <summary>
/// D146 (what is on it: new episodes of Continue Watching shows, playlist releases, followed games,
/// plans — never leaving-soon), D147 (the household calendar labels whose), D148 (timed where a time
/// is known, else all-day), D149 (a moved date moves, it does not duplicate), D244 (the page shows the
/// viewer's own and shared items only), D245 (two weeks ahead) and D242 (held games are absent).
/// Drawn with mockup 8's invented household: Sam and Alex.
/// </summary>
public class CalendarItemsTests
{
    private static readonly ViewerId Sam = new("sam");
    private static readonly ViewerId Alex = new("alex");
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Invented/Harbor", TimeSpan.FromHours(-5), "Invented Harbor", "Invented Harbor");

    private static readonly LeagueId Coastal = new("cfl-invented");
    private static readonly ServiceId Northstar = new("northstar-plus");

    private static EpisodeRelease Episode(string show, DateOnly date, params ViewerId[] watchers) =>
        new(new TitleId(show), show, 1, 4, date, null, Northstar, watchers);

    private static TitleRelease Release(string title, DateOnly date, params PlaylistShelf[] playlists) =>
        new(new TitleId(title), title, TitleKind.Film, date, null, Northstar, playlists);

    private static Fixture Game(string id, DateTimeOffset at, GameStage stage, params ViewerId[] followers) =>
        new(new GameId(id), Coastal, "Harbor Gulls at Ridge Foxes", at, stage, "Gridline TV", followers, FixtureKind.Game);

    private static PlannedViewing Plan(string id, DateOnly date, TimeOnly? time, params ViewerId[] viewers) =>
        new(id, new TitleId("the-majestic"), "The Majestic", date, time, viewers);

    private static CalendarSources Sources(
        IReadOnlyList<EpisodeRelease>? episodes = null,
        IReadOnlyList<TitleRelease>? releases = null,
        IReadOnlyList<Fixture>? games = null,
        IReadOnlyList<PlannedViewing>? plans = null) =>
        new(episodes ?? [], releases ?? [], games ?? [], plans ?? []);

    private static readonly IReadOnlySet<LeagueId> NothingHeld = new HashSet<LeagueId>();
    private static readonly IReadOnlyDictionary<ViewerId, IReadOnlySet<LeagueId>> NobodyHolds =
        new Dictionary<ViewerId, IReadOnlySet<LeagueId>> { [Sam] = NothingHeld, [Alex] = NothingHeld };

    [Fact]
    public void EachKindOfSourceBecomesAnItem()
    {
        var sources = Sources(
            [Episode("Saltmarsh", Today.AddDays(1), Sam)],
            [Release("High Pass", Today.AddDays(5), new PlaylistShelf("Rainy Sundays", Sam, Shared: false))],
            [Game("g-1", new DateTimeOffset(2026, 10, 12, 1, 0, 0, TimeSpan.Zero), GameStage.RegularSeason, Sam)],
            [Plan("plan-1", Today, new TimeOnly(20, 0), Sam, Alex)]);

        var items = CalendarItems.ForViewer(sources, Sam, Today, Zone, NothingHeld);

        Assert.Equal(
            [CalendarItemKind.Plan, CalendarItemKind.NewEpisode, CalendarItemKind.Game, CalendarItemKind.Release],
            items.Select(item => item.Kind));
    }

    [Fact]
    public void LeavingSoonIsNotSomethingTheCalendarCanHold()
    {
        var kinds = Enum.GetNames<CalendarItemKind>();
        Assert.NotEmpty(kinds);
        Assert.DoesNotContain(kinds, name => name.Contains("Leav", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Expir", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ThePageShowsTheViewersOwnAndSharedItemsOnly()
    {
        var sources = Sources(
            [Episode("Saltmarsh", Today.AddDays(1), Sam), Episode("Quarry Road", Today.AddDays(1), Alex)],
            [
                Release("High Pass", Today.AddDays(2), new PlaylistShelf("Rainy Sundays", Alex, Shared: true)),
                Release("Low Tide", Today.AddDays(2), new PlaylistShelf("Alex's own", Alex, Shared: false)),
            ],
            [Game("g-alex", new DateTimeOffset(2026, 10, 12, 1, 0, 0, TimeSpan.Zero), GameStage.RegularSeason, Alex)],
            [Plan("plan-alex", Today, null, Alex), Plan("plan-both", Today, null, Alex, Sam)]);

        var names = CalendarItems.ForViewer(sources, Sam, Today, Zone, NothingHeld).Select(item => item.Key).ToList();

        Assert.Equal(["plan:plan-both", "episode:saltmarsh:1:4", "release:high pass"], names);
    }

    [Fact]
    public void APlaylistReleaseOnThePageNamesOnlyThePlaylistsTheViewerCanSee()
    {
        var sources = Sources(releases:
        [
            Release(
                "High Pass",
                Today.AddDays(2),
                new PlaylistShelf("Rainy Sundays", Alex, Shared: true),
                new PlaylistShelf("Alex's own", Alex, Shared: false)),
        ]);

        var item = Assert.Single(CalendarItems.ForViewer(sources, Sam, Today, Zone, NothingHeld));

        Assert.Equal(["Rainy Sundays"], item.Playlists);
    }

    [Fact]
    public void TheHouseholdCalendarCarriesEveryonesLabelledWithWhose()
    {
        var sources = Sources(
            [Episode("Saltmarsh", Today.AddDays(1), Sam), Episode("Quarry Road", Today.AddDays(1), Alex)],
            releases: [Release("Low Tide", Today.AddDays(2), new PlaylistShelf("Alex's own", Alex, Shared: false))]);

        var items = CalendarItems.ForHousehold(sources, Today, Zone, NobodyHolds);

        Assert.Equal([Alex], items.Single(item => item.Name == "Quarry Road").Whose);
        Assert.Equal([Sam], items.Single(item => item.Name == "Saltmarsh").Whose);
        Assert.Equal([Alex], items.Single(item => item.Name == "Low Tide").Whose);
        Assert.Equal(["Alex's own"], items.Single(item => item.Name == "Low Tide").Playlists);
    }

    [Fact]
    public void OneShowOnBothAdultsListsIsOneItemForBoth()
    {
        var sources = Sources([Episode("Saltmarsh", Today.AddDays(1), Sam), Episode("Saltmarsh", Today.AddDays(1), Alex)]);

        var item = Assert.Single(CalendarItems.ForHousehold(sources, Today, Zone, NobodyHolds));

        Assert.Equal([Alex, Sam], item.Whose);
    }

    [Fact]
    public void AGameIsTimedInTheHouseholdsOwnZone()
    {
        // 01:00 UTC on the 12th is 8 pm on the 11th, five hours behind.
        var sources = Sources(games: [Game("g-1", new DateTimeOffset(2026, 10, 12, 1, 0, 0, TimeSpan.Zero), GameStage.RegularSeason, Sam)]);

        var item = Assert.Single(CalendarItems.ForViewer(sources, Sam, Today, Zone, NothingHeld));

        Assert.Equal(new CalendarWhen(new DateOnly(2026, 10, 11), new TimeOnly(20, 0)), item.When);
        Assert.False(item.When.IsAllDay);
    }

    [Fact]
    public void AnEpisodeIsAllDayUnlessItIsALivePremiere()
    {
        var streamed = Episode("Saltmarsh", Today.AddDays(1), Sam);
        var live = Episode("Quarry Road", Today.AddDays(1), Sam) with
        {
            LivePremiere = new DateTimeOffset(2026, 10, 11, 2, 0, 0, TimeSpan.Zero),
        };

        var items = CalendarItems.ForViewer(Sources([streamed, live]), Sam, Today, Zone, NothingHeld);

        Assert.True(items.Single(item => item.Name == "Saltmarsh").When.IsAllDay);
        Assert.Equal(new TimeOnly(21, 0), items.Single(item => item.Name == "Quarry Road").When.Time);
    }

    [Fact]
    public void APlanIsTimedOnlyWhenItWasGivenATime()
    {
        var items = CalendarItems.ForViewer(
            Sources(plans: [Plan("timed", Today, new TimeOnly(20, 0), Sam), Plan("loose", Today.AddDays(1), null, Sam)]),
            Sam, Today, Zone, NothingHeld);

        Assert.Equal(new TimeOnly(20, 0), items.Single(item => item.Key == "plan:timed").When.Time);
        Assert.True(items.Single(item => item.Key == "plan:loose").When.IsAllDay);
    }

    [Fact]
    public void TheWindowIsTwoWeeksFromToday()
    {
        var sources = Sources(
        [
            Episode("Yesterday", Today.AddDays(-1), Sam),
            Episode("Today", Today, Sam),
            Episode("Last day", Today.AddDays(13), Sam),
            Episode("Too far", Today.AddDays(14), Sam),
        ]);

        var names = CalendarItems.ForViewer(sources, Sam, Today, Zone, NothingHeld).Select(item => item.Name);

        Assert.Equal(["Today", "Last day"], names);
    }

    [Fact]
    public void AMovedDateKeepsItsKeySoTheEventMovesRatherThanDoubles()
    {
        var before = Assert.Single(CalendarItems.ForHousehold(Sources([Episode("Saltmarsh", Today.AddDays(1), Sam)]), Today, Zone, NobodyHolds));
        var after = Assert.Single(CalendarItems.ForHousehold(Sources([Episode("Saltmarsh", Today.AddDays(3), Sam)]), Today, Zone, NobodyHolds));

        Assert.Equal(before.Key, after.Key);
        Assert.NotEqual(before.When, after.When);
    }

    [Fact]
    public void AHeldLeaguesRevealingGamesAreAbsentAndItsRegularSeasonStays()
    {
        // The championship's own line is proved through SportsFollowing.Fixtures in
        // HeldFixturesNameNoTeamsTests; a fixture hand-built here with a safe name proves nothing.
        var at = new DateTimeOffset(2026, 10, 12, 1, 0, 0, TimeSpan.Zero);
        var sources = Sources(games:
        [
            Game("playoff", at, GameStage.Playoffs, Sam),
            Game("final", at.AddDays(1), GameStage.Championship, Sam),
            Game("regular", at, GameStage.RegularSeason, Sam),
        ]);

        var held = new HashSet<LeagueId> { Coastal };
        var keys = CalendarItems.ForViewer(sources, Sam, Today, Zone, held).Select(item => item.Key).ToList();

        Assert.Equal(["game:regular"], keys);
        Assert.Equal(3, CalendarItems.ForViewer(sources, Sam, Today, Zone, NothingHeld).Count);
    }

    [Fact]
    public void OneAdultsHoldKeepsTheOtherAdultsSpoilerOffTheHouseholdCalendar()
    {
        // Sam is behind on the league; Alex is not, and follows the playoff game. The household
        // calendar is printed for both, so Sam's hold must keep Alex's playoff fixture off it.
        var at = new DateTimeOffset(2026, 10, 12, 1, 0, 0, TimeSpan.Zero);
        var sources = Sources(games:
        [
            Game("playoff", at, GameStage.Playoffs, Alex),
            Game("regular", at, GameStage.RegularSeason, Alex),
        ]);

        var heldByAdult = new Dictionary<ViewerId, IReadOnlySet<LeagueId>>
        {
            [Sam] = new HashSet<LeagueId> { Coastal },
            [Alex] = new HashSet<LeagueId>(),
        };

        var keys = CalendarItems.ForHousehold(sources, Today, Zone, heldByAdult).Select(item => item.Key).ToList();

        Assert.Equal(["game:regular"], keys);
    }

    [Fact]
    public void TheHouseholdCalendarRefusesAFollowerWhoseHoldsItWasNotGiven()
    {
        // An adult missing from the holds is not "holding nothing": it is not knowing. Fail loudly.
        var at = new DateTimeOffset(2026, 10, 12, 1, 0, 0, TimeSpan.Zero);
        var sources = Sources(games: [Game("playoff", at, GameStage.Playoffs, Alex)]);
        var heldByAdult = new Dictionary<ViewerId, IReadOnlySet<LeagueId>>
        {
            [Sam] = new HashSet<LeagueId> { Coastal },
        };

        var error = Assert.Throws<ArgumentException>(() => CalendarItems.ForHousehold(sources, Today, Zone, heldByAdult));
        Assert.Contains("alex", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADayListsAllDayItemsFirstThenByTime()
    {
        var sources = Sources(
            [Episode("Saltmarsh", Today, Sam)],
            plans: [Plan("late", Today, new TimeOnly(21, 0), Sam), Plan("early", Today, new TimeOnly(19, 0), Sam)]);

        var days = CalendarItems.ByDay(CalendarItems.ForViewer(sources, Sam, Today, Zone, NothingHeld));

        var day = Assert.Single(days);
        Assert.Equal(Today, day.Date);
        Assert.Equal(["episode:saltmarsh:1:4", "plan:early", "plan:late"], day.Items.Select(item => item.Key));
    }
}
