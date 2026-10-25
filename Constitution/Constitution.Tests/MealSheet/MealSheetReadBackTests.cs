using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;

namespace Constitution.Tests.MealSheet;

/// <summary>C-2.4.1: the expected document the read-back reads the dinners page by.</summary>
public class MealSheetReadBackTests
{
    private static readonly DayOfWeek[] Week =
        [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    private static ExpectedDocument Expected() => ExpectedDocument.ForMealSheet(MealSheetSamples.Model(), "Sam");

    [Fact]
    public void ItIsAMealsDocumentWithOnePageToRead()
    {
        var document = Expected();

        Assert.Equal(DeliveryKind.Meals, document.Kind);
        Assert.Equal(MealSheetSamples.Sunday, document.Day);
        Assert.Equal("Sam", document.Person);
        Assert.True(document.ReadsInk);
        Assert.Null(document.Session);
        Assert.Equal(DeliveredName.For(DeliveryKind.Meals, MealSheetSamples.Sunday), document.Name);
        Assert.Equal(1, document.Pages.Single().Number);
    }

    [Fact]
    public void EveryRowIsAMealItemRuleWithTheDeadline()
    {
        var page = Expected().Pages.Single();
        var rules = page.Rules.Cast<MealItemRule>().ToList();

        Assert.Equal(7, rules.Count);
        Assert.Equal(Week.Select(MealSheetIds.DinnerYes), rules.Select(r => r.YesBoxId));
        Assert.Equal(Week.Select(MealSheetIds.DinnerReason), rules.Select(r => r.ReasonId));
        Assert.All(rules, r => Assert.Equal(MealSheetSamples.Deadline, r.Deadline));
        Assert.Contains("Sunday", rules[0].Subject);
        Assert.Contains("Veggie fried rice", rules[0].Subject);
        Assert.Contains("Monday", rules[1].Subject);
        Assert.Contains("Lentil soup", rules[1].Subject);
        Assert.Contains("Corner noodle house", rules[5].Subject);
    }

    [Fact]
    public void BothEndsOfEveryRowAreAsked()
    {
        var page = Expected().Pages.Single();
        var asked = page.AskedTargets.Select(t => t.Id).ToList();

        Assert.Equal(14, asked.Count);
        Assert.All(Week, day =>
        {
            Assert.Contains(MealSheetIds.DinnerYes(day), asked);
            Assert.Contains(MealSheetIds.DinnerReason(day), asked);
        });
        Assert.All(page.Rules, rule => Assert.Equal(2, RuleTargets.For(rule, page.Readout.Map).Count));
        Assert.All(page.Rules, rule => Assert.Single(RuleTargets.SubjectIds(rule)));
    }

    [Fact]
    public void RulesNameTargetsThePageActuallyHas()
    {
        var page = Expected().Pages.Single();
        var layout = MealSheetDocument.Build(MealSheetSamples.Model()).Single();

        Assert.All(page.Rules.Cast<MealItemRule>(), rule =>
        {
            Assert.NotNull(layout.Map.TryGet(rule.YesBoxId));
            Assert.NotNull(layout.Map.TryGet(rule.ReasonId));
        });
    }

    [Fact]
    public void TheRuleCarriesTheDeadline()
    {
        var page = Expected().Pages.Single();
        var rule = (MealItemRule)page.Rules[0];

        Assert.Equal(MealSheetIds.DinnerYesSunday, rule.YesBoxId);
        Assert.Equal(MealSheetIds.DinnerReasonSunday, rule.ReasonId);
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.FromHours(-5)), rule.Deadline);
    }

    [Fact]
    public void ANoPlanWeekHasNothingToRead()
    {
        var model = MealSheetSamples.Model() with
        {
            Week = Constitution.Domain.Meals.MealWeek.NotPlanned(MealSheetSamples.Sunday, Constitution.Domain.Meals.MealStop.BoxTooSmall, "Too few cards."),
        };

        Assert.Throws<ArgumentException>(() => ExpectedDocument.ForMealSheet(model, "Sam"));
    }
}
