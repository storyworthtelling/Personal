using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;

namespace Constitution.Tests.MealSheet;

/// <summary>C-2.4.2: the expected document reads pages 2 and 3 too, so every line is approved or denied on its own (D30).</summary>
public class MealSheetDailyReadBackTests
{
    private static ExpectedDocument Expected(MealSheetDailyMeals? daily = null) =>
        ExpectedDocument.ForMealSheet(MealSheetSamples.Model(daily: daily ?? MealSheetSamples.Daily()), "Sam");

    [Fact]
    public void ThreePagesAreRead()
    {
        var document = Expected();

        Assert.Equal([1, 2, 3], document.Pages.Select(p => p.Number));
        Assert.Equal([7, 7, 5], document.Pages.Select(p => p.Rules.Count));
        Assert.All(document.Pages.SelectMany(p => p.Rules), rule => Assert.IsType<MealItemRule>(rule));
    }

    [Fact]
    public void WithoutDailyMealsItIsStillOnePage() =>
        Assert.Single(ExpectedDocument.ForMealSheet(MealSheetSamples.Model(), "Sam").Pages);

    [Fact]
    public void EveryRuleNamesTargetsItsPageHas()
    {
        var document = Expected();
        var layouts = MealSheetDocument.Build(MealSheetSamples.Model(daily: MealSheetSamples.Daily()));

        for (var index = 0; index < 3; index++)
        {
            foreach (var rule in document.Pages[index].Rules.Cast<MealItemRule>())
            {
                Assert.NotNull(layouts[index].Map.TryGet(rule.YesBoxId));
                Assert.NotNull(layouts[index].Map.TryGet(rule.ReasonId));
                Assert.Equal(MealSheetSamples.Deadline, rule.Deadline);
            }
        }
    }

    [Fact]
    public void BothEndsOfEveryLineAreAsked()
    {
        var document = Expected();

        Assert.Equal([14, 14, 10], document.Pages.Select(p => p.AskedTargets.Count));
        Assert.All(document.Pages.SelectMany(p => p.Rules), rule => Assert.Single(RuleTargets.SubjectIds(rule)));
    }

    [Fact]
    public void SubjectsSayWhatTheLineIs()
    {
        var rules = Expected().Pages.Skip(1).SelectMany(p => p.Rules.Cast<MealItemRule>()).ToList();

        Assert.Equal("Weekday breakfast, Sam: Overnight oats, berries", rules[0].Subject);
        Assert.Equal(MealSheetIds.BreakfastYes(1), rules[0].YesBoxId);
        Assert.Equal("Weekday lunch, Lee: Chickpea salad, crackers", rules[4].Subject);
        Assert.Equal(MealSheetIds.LunchReason(2), rules[4].ReasonId);
        Assert.Equal("Weekend breakfast: Banana pancakes, everyone", rules[5].Subject);
        Assert.Equal("Weekend lunch: Cheese quesadillas, salsa, fruit", rules[6].Subject);
        Assert.Equal("Lunchbox sandwich: Sunflower butter and jam", rules[7].Subject);
        Assert.Equal("Lunchbox separate bar: Oat granola bar", rules[11].Subject);
        Assert.Equal(MealSheetIds.SnackBarYes, rules[11].YesBoxId);
    }

    [Fact]
    public void AnUntouchedLineIsApprovedAtTheDeadline()
    {
        var page = Expected().Pages[2];
        var resolver = new InkResolver(page.Readout);
        var reads = page.Readout.Map.Targets.Select(t => InkFixture(t.Id, false)).ToList();

        var resolved = resolver.Resolve(InkPageRead.Read(reads), page.Rules, MealSheetSamples.Deadline);

        Assert.Equal(5, resolved.Count);
        Assert.All(resolved, r => Assert.IsType<InkResolution.ApprovedAtDeadline>(r));
    }

    [Fact]
    public void AWrittenReasonDeniesThatLineAlone()
    {
        var page = Expected().Pages[1];
        var resolver = new InkResolver(page.Readout);
        var reads = page.Readout.Map.Targets
            .Select(t => t.Id == MealSheetIds.WeekendLunchReason ? new InkTargetRead(t.Id, true, 0.95, "No cheese this week") : InkFixture(t.Id, false))
            .ToList();

        var resolved = resolver.Resolve(InkPageRead.Read(reads), page.Rules, MealSheetSamples.Deadline);

        Assert.Equal(new InkResolution.Denied(MealSheetIds.WeekendLunchYes, "No cheese this week"), resolved.Single(r => r is InkResolution.Denied));
        Assert.Equal(6, resolved.Count(r => r is InkResolution.ApprovedAtDeadline));
    }

    private static InkTargetRead InkFixture(string id, bool marked) => new(id, marked, 0.95);
}
