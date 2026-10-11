using Pulp.Domain.Sports;
using Pulp.Infrastructure.Sports;

namespace Pulp.Tests.Infrastructure.Sports;

public class RoutedSportsSourceTests
{
    private sealed class Fake(string name, params string[] leagues) : ISportsSource
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<LeagueId> Leagues { get; } = leagues.Select(id => new LeagueId(id)).ToList();

        public Task<LeagueSchedule> GetScheduleAsync(LeagueId league, SportsWindow window, CancellationToken cancellationToken)
        {
            Calls.Add(name + ":" + league);
            return Task.FromResult(new LeagueSchedule(
                new League(league, name, "badge://x", SeasonPhase.RegularSeason, new DateOnly(2026, 2, 1), null), [], [], null));
        }

        public Task<IReadOnlyList<Team>> GetStandingsOrderAsync(LeagueId league, CancellationToken cancellationToken)
        {
            Calls.Add(name + "-standings:" + league);
            return Task.FromResult<IReadOnlyList<Team>>([]);
        }
    }

    private static SportsSourceTable Table() => new(
        [new SportsSourceInfo("one", "One", "t"), new SportsSourceInfo("two", "Two", "t")],
        [new SportsLeagueEntry(new LeagueId("a-invented"), "A", "one"), new SportsLeagueEntry(new LeagueId("b-invented"), "B", "two")]);

    [Fact]
    public async Task EachLeagueIsAskedOfTheSourceTheTableNamesForIt()
    {
        var one = new Fake("one", "a-invented");
        var two = new Fake("two", "b-invented");
        var routed = new RoutedSportsSource(Table(), new Dictionary<string, ISportsSource> { ["one"] = one, ["two"] = two });

        await routed.GetScheduleAsync(new LeagueId("B-invented"), SportsWindow.Around(new DateOnly(2026, 1, 1)), CancellationToken.None);
        await routed.GetStandingsOrderAsync(new LeagueId("a-invented"), CancellationToken.None);

        Assert.Equal(["two:B-invented"], two.Calls);
        Assert.Equal(["one-standings:a-invented"], one.Calls);
        Assert.Equal(["a-invented", "b-invented"], routed.Leagues.Select(id => id.Value));
    }

    [Fact]
    public async Task ALeagueNotInTheTableIsRefused()
    {
        var routed = new RoutedSportsSource(Table(), new Dictionary<string, ISportsSource> { ["one"] = new Fake("one"), ["two"] = new Fake("two") });

        await Assert.ThrowsAsync<ArgumentException>(() => routed.GetStandingsOrderAsync(new LeagueId("zzz"), CancellationToken.None));
    }

    [Fact]
    public void ATableNamingASourceWithNoAdapterIsRefusedWhenBuilt()
    {
        var thrown = Assert.Throws<ArgumentException>(() => new RoutedSportsSource(Table(), new Dictionary<string, ISportsSource> { ["one"] = new Fake("one") }));
        Assert.Contains("two", thrown.Message);
    }
}
