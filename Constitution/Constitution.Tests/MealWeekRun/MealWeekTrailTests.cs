using Constitution.Domain.Diagnostics;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.MealWeekRun;

/// <summary>The trail's lines for the meal run: fixed words and counts only, each one past the export's filter whole.</summary>
public class MealWeekTrailTests
{
    private const string Stamp = "[Oct 21, 12:05 pm] INFO Body — ";

    private static MealWeekReport Report(PlacementRun? placement = null, MealRecordSave save = MealRecordSave.Saved, bool earlier = false) => new(
        MealWeek.Planned(Sunday, Enumerable.Range(0, 7).Select(offset => DinnerChoice.NothingPlanned(Sunday.AddDays(offset), "Nothing is planned."))),
        PlanOrigin.RulesOnly,
        null,
        [],
        placement ?? new PlacementRun(null, [], [], null),
        true,
        save,
        earlier);

    public static IEnumerable<object[]> EveryPhrase()
    {
        foreach (var because in Enum.GetValues<MealNotPlannedBecause>())
        {
            yield return [new MealWeekOutcome.NotPlanned(because, "A sentence.")];
        }

        yield return [new MealWeekOutcome.Planned(Report())];
        yield return [new MealWeekOutcome.Planned(Report(save: MealRecordSave.Unchanged))];
        yield return [new MealWeekOutcome.Planned(Report(earlier: true))];
        yield return [new MealWeekOutcome.Planned(Report() with { ExtraLinesUnreadable = "The record could not be read." })];
        yield return [new MealWeekOutcome.Planned(Report(new PlacementRun(null, [], [], "The record could not be read.")))];
    }

    [Theory]
    [MemberData(nameof(EveryPhrase))]
    public void EveryOutcomeHasAPhraseTheFilterKeepsWhole(MealWeekOutcome outcome)
    {
        var line = Stamp + MealWeekTrail.LogPhrase(outcome);

        Assert.Equal(line, TrailLines.Filter(line));
    }

    [Theory]
    [InlineData(PlanOrigin.RulesOnly, null)]
    [InlineData(PlanOrigin.Authored, null)]
    [InlineData(PlanOrigin.RulesOnly, AuthorUnavailable.NotConfigured)]
    [InlineData(PlanOrigin.RulesOnly, AuthorUnavailable.BrokeTheRules)]
    [InlineData(PlanOrigin.RulesOnly, AuthorUnavailable.KeyRejected)]
    public void TheOriginLineSurvivesTheFilter(PlanOrigin origin, AuthorUnavailable? reason)
    {
        var line = Stamp + MealWeekTrail.LogPhrase(origin, reason);

        Assert.StartsWith("meal plan: ", MealWeekTrail.LogPhrase(origin, reason), StringComparison.Ordinal);
        Assert.Equal(line, TrailLines.Filter(line));
    }

    [Fact]
    public void ThePhrasesCarryNoNameNoDishAndNoDate()
    {
        var all = EveryPhrase().Select(row => MealWeekTrail.LogPhrase((MealWeekOutcome)row[0])).ToList();

        Assert.All(all, phrase =>
        {
            Assert.DoesNotContain(Sam, phrase, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Lee, phrase, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("2026", phrase, StringComparison.Ordinal);
            Assert.Equal(phrase, phrase.ToLowerInvariant());
        });
    }

    [Fact]
    public void ThePlannedPhraseCountsEventsWrittenAndFailed()
    {
        var created = new EventToCreate(PlacementKey.ForHousehold(Sam, 1, "cook-tue"), Sam, "Cook: rice", Edt(10, 27, 17), Edt(10, 27, 18));
        var failed = new CreateFailure(created, "refused");

        var phrase = MealWeekTrail.LogPhrase(new MealWeekOutcome.Planned(Report(new PlacementRun(null, [created, created], [failed], null))));

        Assert.Equal("meal week planned, 2 events written, 1 events failed, record saved", phrase);
    }

    [Fact]
    public void ABlockedPlacementAndAnEarlierRecordAreSaid()
    {
        var phrase = MealWeekTrail.LogPhrase(new MealWeekOutcome.Planned(Report(new PlacementRun(null, [], [], "Could not read."), MealRecordSave.Unchanged, earlier: true)));

        Assert.Equal("meal week planned, 0 events written, 0 events failed, record unchanged, placement blocked, earlier record unreadable", phrase);
    }

    [Fact]
    public void AnUnreadableDecisionRecordIsSaidWithoutItsReason()
    {
        var phrase = MealWeekTrail.LogPhrase(new MealWeekOutcome.Planned(Report() with { ExtraLinesUnreadable = "The record could not be read." }));

        Assert.Equal("meal week planned, 0 events written, 0 events failed, record saved, decisions unreadable", phrase);
    }

    [Fact]
    public void TheNotThePlannerPhraseIsTheOneTheRigExpects()
    {
        Assert.Equal(
            "meal week not planned, not the planner this week",
            MealWeekTrail.LogPhrase(new MealWeekOutcome.NotPlanned(MealNotPlannedBecause.NotThePlanner, "x")));
    }
}
