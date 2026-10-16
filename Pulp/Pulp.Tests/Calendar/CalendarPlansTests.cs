using Pulp.Domain.Calendar;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;

namespace Pulp.Tests.Calendar;

/// <summary>
/// Plans (D68, D146, D244): a cinema outing or "plan to watch" on the shared calendar. The viewer's own or
/// shared, adults only — a child is never on one. Invented names throughout.
/// </summary>
public class CalendarPlansTests
{
    private static readonly ViewerId Sam = new("sam");
    private static readonly ViewerId Alex = new("alex");
    private static readonly ViewerId Pip = new("pip");
    private static readonly IReadOnlySet<ViewerId> Adults = new HashSet<ViewerId> { Sam, Alex };
    private static readonly DateOnly Today = new(2026, 10, 12);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    private static HouseholdPlan Plan(string id, string text, params ViewerId[] who) =>
        new(id, Sam, text, null, new DateOnly(2026, 10, 12), new TimeOnly(20, 0), "The Majestic", who);

    [Fact]
    public void APlanBecomesAPlannedViewingForItsAdultsOnly()
    {
        var sources = CalendarPlans.ToSources([Plan("a1", "Saltmarsh at the cinema", Sam, Alex, Pip)], Adults);

        var plan = Assert.Single(sources);
        Assert.Equal("a1", plan.PlanId);
        Assert.Equal("Saltmarsh at the cinema", plan.Name);
        Assert.Equal([Sam, Alex], plan.Viewers);
        Assert.Equal("The Majestic", plan.Place);
        Assert.Null(plan.Title);
    }

    [Fact]
    public void APlanOnlyAChildIsOnNeverReachesTheCalendar()
    {
        Assert.Empty(CalendarPlans.ToSources([Plan("a1", "Film night", Pip)], Adults));
    }

    [Fact]
    public void ThePageShowsAPlanToThoseInItAndTheHouseholdCalendarToEveryone()
    {
        var sources = new CalendarSources([], [], [], CalendarPlans.ToSources(
            [Plan("mine", "Mine", Sam), Plan("hers", "Hers", Alex), Plan("ours", "Ours", Sam, Alex)], Adults));

        var forAlex = CalendarItems.ForViewer(sources, Alex, Today, Zone, new HashSet<Pulp.Domain.Sports.LeagueId>());
        var household = CalendarItems.ForHousehold(sources, Today, Zone, new Dictionary<ViewerId, IReadOnlySet<Pulp.Domain.Sports.LeagueId>>());

        Assert.Equal(["Hers", "Ours"], forAlex.Select(item => item.Name).Order());
        Assert.Equal(3, household.Count);
        Assert.All(household, item => Assert.Equal(CalendarItemKind.Plan, item.Kind));
    }

    [Fact]
    public void APlansPlaceIsItsDetailAndItsKeyNamesTheRecord()
    {
        var sources = new CalendarSources([], [], [], CalendarPlans.ToSources([Plan("A1B2", "Film night", Sam)], Adults));

        var item = Assert.Single(CalendarItems.ForViewer(sources, Sam, Today, Zone, new HashSet<Pulp.Domain.Sports.LeagueId>()));

        Assert.Equal("The Majestic", item.Detail);
        Assert.Equal("plan:a1b2", item.Key);
        Assert.Equal("a1b2", CalendarPlans.IdOfKey(item.Key));
        Assert.Null(CalendarPlans.IdOfKey("episode:x:1:1"));
    }

    [Fact]
    public void AFreeTextPlanReadsAsAPlanWithItsPlaceAndAPlanForATitleKeepsItsKindLine()
    {
        var context = new CalendarScreenContext(Sam, id => id.Value, _ => null, _ => null);
        var free = CalendarItems.ForViewer(
            new CalendarSources([], [], [], CalendarPlans.ToSources([Plan("a1", "Film night", Sam)], Adults)),
            Sam, Today, Zone, new HashSet<Pulp.Domain.Sports.LeagueId>());

        var row = Assert.Single(Assert.Single(CalendarScreen.Build(free, context, new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero), Zone).Days).Rows);

        Assert.Equal("Plan · Film night", row.Title);
        Assert.Equal("Plan · The Majestic", row.Detail);
        Assert.Null(row.OpenTitle);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Film night", true)]
    public void APlanNeedsWords(string text, bool fine) =>
        Assert.Equal(fine, CalendarPlans.Check(text, Today, Today) is null);

    [Fact]
    public void APlanCannotBeInThePastOrAbsurdlyLong()
    {
        Assert.NotNull(CalendarPlans.Check("Film night", Today.AddDays(-1), Today));
        Assert.Null(CalendarPlans.Check("Film night", Today, Today));
        Assert.NotNull(CalendarPlans.Check(new string('x', CalendarPlans.LongestText + 1), Today, Today));
    }

    [Fact]
    public void CleaningTrimsAndCollapsesSpacesAndSharedMeansEveryAdult()
    {
        Assert.Equal("Film night", CalendarPlans.Clean("  Film   night "));
        Assert.Null(CalendarPlans.Clean("  "));
        Assert.Equal([Sam], CalendarPlans.Who(Sam, shared: false, [Sam, Alex]));
        Assert.Equal([Sam, Alex], CalendarPlans.Who(Sam, shared: true, [Alex, Sam]).OrderBy(who => who == Sam ? 0 : 1));
    }

    [Fact]
    public void AMintedIdIsLowerCaseHexAndNewEachTime()
    {
        var one = CalendarPlans.NewId();
        Assert.Matches("^[0-9a-f]{32}$", one);
        Assert.NotEqual(one, CalendarPlans.NewId());
    }

    [Fact]
    public void AScreenSourcesBuildCarriesThePlansFromTheRecords()
    {
        var reads = new CalendarScreenReads(
            new Dictionary<ViewerId, IReadOnlyList<TitleId>> { [Sam] = [], [Alex] = [] }, [], new Dictionary<TitleId, Pulp.Domain.Catalogue.CatalogueTitle>(),
            _ => null, Zone, Plans: [Plan("a1", "Film night", Sam, Pip)]);

        var plan = Assert.Single(CalendarScreenSources.Build(reads, []).Plans);

        Assert.Equal([Sam], plan.Viewers);
    }
}
