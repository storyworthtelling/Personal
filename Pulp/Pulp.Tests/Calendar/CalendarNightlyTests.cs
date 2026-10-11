using Pulp.Domain.Calendar;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Sports;

namespace Pulp.Tests.Calendar;

/// <summary>The nightly pass end to end over fakes: who writes, and from what.</summary>
public class CalendarNightlyTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly ViewerId Sam = new("sam");
    private static readonly ViewerId Alex = new("alex");

    private sealed class FakeFeed : ICalendarFeed
    {
        public required CalendarFeedSnapshot Snapshot { get; init; }

        public List<string> Saved { get; } = [];

        public Task<CalendarFeedSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);

        public Task SaveCalendarIdAsync(string calendarId, CancellationToken cancellationToken)
        {
            Saved.Add(calendarId);
            return Task.CompletedTask;
        }
    }

    private static CalendarFeedSnapshot Snapshot(HouseholdFileRole role, string? calendarId, CalendarSources? sources = null) => new(
        sources ?? new CalendarSources(
            [new EpisodeRelease(new TitleId("harbor-lights"), "Harbor Lights", 1, 4, Today.AddDays(2), null, new ServiceId("northstar-plus"), [Sam])],
            [],
            [],
            []),
        new Dictionary<ViewerId, IReadOnlySet<LeagueId>> { [Sam] = new HashSet<LeagueId>(), [Alex] = new HashSet<LeagueId>() },
        viewer => viewer == Sam ? "Sam" : "Alex",
        role,
        calendarId,
        "owner@example.com",
        ["owner@example.com", "partner@example.com"],
        TimeZoneInfo.Utc);

    [Fact]
    public async Task TheOwnerCreatesSavesSharesAndWritesTheHouseholdsItems()
    {
        var feed = new FakeFeed { Snapshot = Snapshot(HouseholdFileRole.Owner, null) };
        var calendar = new FakePulpCalendar();

        var outcome = await CalendarNightly.RunAsync(feed, calendar, Today, CancellationToken.None);

        Assert.Equal(["cal-new"], feed.Saved);
        Assert.Equal(["partner@example.com"], calendar.Shared);
        Assert.Equal(["insert:episode:harbor-lights:1:4"], calendar.Calls);
        Assert.Equal(1, outcome.Result!.Created);
    }

    [Theory]
    [InlineData(HouseholdFileRole.SharedWith)]
    [InlineData(HouseholdFileRole.Unknown)]
    public async Task NoOneButTheOwnerWritesAnything(HouseholdFileRole role)
    {
        var feed = new FakeFeed { Snapshot = Snapshot(role, "cal-1") };
        var calendar = new FakePulpCalendar();

        var outcome = await CalendarNightly.RunAsync(feed, calendar, Today, CancellationToken.None);

        Assert.Null(outcome.Result);
        Assert.Empty(calendar.Calls);
        Assert.Empty(calendar.Shared);
        Assert.Equal(0, calendar.Creates);
        Assert.Empty(feed.Saved);
    }

    [Fact]
    public async Task AnAdultWhoseHoldsWereNotGivenFailsLoudly()
    {
        var game = new Fixture(
            new GameId("g1"), new LeagueId("cfl-invented"), "Harbor Gulls at Ridge Foxes", new DateTimeOffset(2026, 10, 12, 1, 0, 0, TimeSpan.Zero),
            GameStage.RegularSeason, "Gridline TV", [Alex], FixtureKind.Game);
        var snapshot = Snapshot(HouseholdFileRole.Owner, "cal-1", new CalendarSources([], [], [game], []));
        snapshot = snapshot with { HeldByAdult = new Dictionary<ViewerId, IReadOnlySet<LeagueId>> { [Sam] = new HashSet<LeagueId>() } };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CalendarNightly.RunAsync(new FakeFeed { Snapshot = snapshot }, new FakePulpCalendar(), Today, CancellationToken.None));
    }
}
