using Constitution.Domain.Calendar;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Constitution.Tests.Calendar;
using Constitution.Tests.Templates;
using static Constitution.Tests.TemplatesInTheRun.TemplateRunData;

namespace Constitution.Tests.TemplatesInTheRun;

/// <summary>The templates step's pure rules: the labels, what to ask the author for, and when a library is the same library.</summary>
public class TemplateRunRulesTests
{
    [Fact]
    public void LabelsAreTheAdultsInTheirOrder() =>
        Assert.Equal(["Sam", "Lee"], TemplateRunRules.Labels(InventedFamily.Rules()));

    [Fact]
    public void LabelsStopAtTheSheetsFiveLines()
    {
        var adults = Enumerable.Range(1, 7).Select(number => new Adult($"Guest{number}", true, [$"guest{number}@example.com"])).ToList();

        var labels = TemplateRunRules.Labels(InventedFamily.Rules() with { Adults = adults });

        Assert.Equal(5, labels.Count);
        Assert.Equal("Guest1", labels[0]);
    }

    [Fact]
    public void AnEmptyLibraryWantsEverySlot()
    {
        var want = TemplateRunRules.Wants(TemplateLibrary.Empty, 2, 2);

        Assert.Equal(Enum.GetValues<MealTemplateSlot>(), want.Slots);
        Assert.Equal(TemplateProposals.MaxOutstanding, want.Count);
    }

    [Fact]
    public void AStockedLibraryWantsNothing() =>
        Assert.Equal(0, TemplateRunRules.Wants(Full(), 2, 2).Count);

    [Fact]
    public void ASlotOneShortIsWantedOnce()
    {
        var shortBy = new TemplateLibrary(Full().Templates.Where(t => t.Id != "tpl-box-fruit-oats").ToList(), []);

        var want = TemplateRunRules.Wants(shortBy, 2, 2);

        Assert.Equal([MealTemplateSlot.BoxFruit], want.Slots);
        Assert.Equal(1, want.Count);
    }

    [Fact]
    public void WaitingProposalsCountTowardsTheNeed()
    {
        var library = Without(MealTemplateSlot.BoxSnack)
            .With(TemplateData.Proposed("tpl-box-snack-a", MealTemplateSlot.BoxSnack, "Rice cakes"))
            .With(TemplateData.Proposed("tpl-box-snack-b", MealTemplateSlot.BoxSnack, "Cheese cubes"))
            .With(TemplateData.Proposed("tpl-box-snack-c", MealTemplateSlot.BoxSnack, "Apple slices"));

        Assert.Equal(0, TemplateRunRules.Wants(library, 2, 2).Count);
    }

    [Fact]
    public void TheCapOfWaitingProposalsStopsTheAsk()
    {
        var waiting = Enum.GetValues<MealTemplateSlot>().Take(TemplateProposals.MaxOutstanding)
            .Select((slot, index) => TemplateData.Proposed($"tpl-wait-{index}", slot, $"Waiting dish {index}")).ToArray();
        var library = new TemplateLibrary([.. Full().Templates, .. waiting], []);
        var shortBy = new TemplateLibrary(library.Templates.Where(t => t.Id != "tpl-snack-bar-oats").ToList(), []);

        Assert.Equal(TemplateProposals.MaxOutstanding, shortBy.Proposed.Count);
        Assert.Equal(0, TemplateRunRules.Wants(shortBy, 2, 2).Count);
    }

    [Fact]
    public void TheRoomLeftLimitsTheCount()
    {
        var waiting = Enumerable.Range(0, 4).Select(index => TemplateData.Proposed($"tpl-wait-{index}", MealTemplateSlot.SnackBar, $"Waiting dish {index}")).ToArray();
        var library = new TemplateLibrary([.. waiting], []);

        Assert.Equal(2, TemplateRunRules.Wants(library, 2, 2).Count);
    }

    [Fact]
    public void MoreWeekdayLinesNeedMoreInThePool()
    {
        var want = TemplateRunRules.Wants(Full(), 5, 2);

        Assert.Equal([MealTemplateSlot.WeekdayBreakfast], want.Slots);
        Assert.Equal(2, want.Count);
    }

    [Fact]
    public void TheSameContentIsTheSameLibrary()
    {
        Assert.True(TemplateRunRules.Same(Full(), Full()));
        Assert.True(TemplateRunRules.Same(Full().WithPrints([new TemplatePrint(Week, "box-fruit-yes", "tpl-box-fruit-oats")]), Full().WithPrints([new TemplatePrint(Week, "box-fruit-yes", "tpl-box-fruit-oats")])));
    }

    [Fact]
    public void APrintOrAStatusMakesItDifferent()
    {
        var printed = Full().WithPrints([new TemplatePrint(Week, "box-fruit-yes", "tpl-box-fruit-oats")]);
        var rejected = Full().With(TemplateData.Rejected("tpl-box-fruit-oats", MealTemplateSlot.BoxFruit, "oats for box-fruit"));

        Assert.False(TemplateRunRules.Same(Full(), printed));
        Assert.False(TemplateRunRules.Same(Full(), rejected));
        Assert.False(TemplateRunRules.Same(Full(), TemplateLibrary.Empty));
    }
}
