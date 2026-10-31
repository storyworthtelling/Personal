using Constitution.Domain.Calendar;
using Constitution.Domain.Meals.Templates;
using static Constitution.Tests.MealAnswers.MealAnswerData;
using static Constitution.Tests.Templates.TemplateData;

namespace Constitution.Tests.Templates;

/// <summary>The rotation over approved templates: stable per week, never an immediate repeat, never a rejected or unanswered one.</summary>
public class TemplateRotationTests
{
    private static string[] Texts(TemplatePicks picks) =>
    [
        .. picks.Daily!.WeekdayBreakfasts.Select(line => line.Text),
        .. picks.Daily.WeekdayLunches.Select(line => line.Text),
        picks.Daily.WeekendBreakfast,
        picks.Daily.WeekendLunch,
        picks.Daily.Lunchbox.Sandwich,
        picks.Daily.Lunchbox.Fruit,
        picks.Daily.Lunchbox.Snack,
        picks.Daily.Lunchbox.SeparateFruitOrVeg,
        picks.Daily.Lunchbox.SeparateBar,
    ];

    [Fact]
    public void TheSameWeekPicksTheSame()
    {
        var first = TemplateRotation.Pick(Full(), Week, Facts);
        var again = TemplateRotation.Pick(Full(), Week, Facts);

        Assert.Equal(TemplatePickStatus.Ready, first.Status);
        Assert.Equal(Texts(first), Texts(again));
        Assert.Equal(first.Prints, again.Prints);
    }

    [Fact]
    public void ANeighbouringWeekPicksDifferently()
    {
        var one = TemplateRotation.Pick(Full(), Week, Facts);
        var next = TemplateRotation.Pick(Full(), WeekAfter(1), Facts);

        Assert.Equal(one.Daily!.WeekendBreakfast == next.Daily!.WeekendBreakfast, false);
        Assert.NotEqual(one.Daily.Lunchbox.Sandwich, next.Daily.Lunchbox.Sandwich);
    }

    [Fact]
    public void NoLineRepeatsTwoWeeksRunning()
    {
        var previous = TemplateRotation.Pick(Full(), Week, Facts);
        for (var weeks = 1; weeks <= 30; weeks++)
        {
            var current = TemplateRotation.Pick(Full(), WeekAfter(weeks), Facts);
            foreach (var print in current.Prints)
            {
                var before = previous.Prints.Single(item => item.ItemId == print.ItemId);
                Assert.NotEqual(before.TemplateId, print.TemplateId);
            }

            previous = current;
        }
    }

    [Fact]
    public void LastWeeksPrintKeepsAChangedPoolFromRepeating()
    {
        var library = Full();
        var plain = TemplateRotation.Pick(library, Week, Facts);
        var chosen = plain.Prints.Single(print => print.ItemId == "weekend-lunch-yes");

        var withMemory = TemplateRotation.Pick(library, Week, Facts with { LastWeek = [chosen with { Week = WeekAfter(-1) }] });

        Assert.NotEqual(chosen.TemplateId, withMemory.Prints.Single(print => print.ItemId == "weekend-lunch-yes").TemplateId);
    }

    [Fact]
    public void ALoneApprovedTemplateIsAllowedToRepeat()
    {
        var lone = Approved("tpl-only-soup", MealTemplateSlot.WeekendLunch, "soup and bread");
        var library = new TemplateLibrary([.. Full().Templates.Where(template => template.Slot != MealTemplateSlot.WeekendLunch), lone], []);

        var picks = TemplateRotation.Pick(library, Week, Facts with { LastWeek = [new TemplatePrint(WeekAfter(-1), "weekend-lunch-yes", lone.Id)] });

        Assert.Equal("soup and bread", picks.Daily!.WeekendLunch);
    }

    [Fact]
    public void ARejectedTemplateIsNeverPrinted()
    {
        var library = new TemplateLibrary(
            [.. Full().Templates, Rejected("tpl-weekend-lunch-bad", MealTemplateSlot.WeekendLunch, "cold liver", "Robin will not eat it")],
            []);

        for (var weeks = 0; weeks < 40; weeks++)
        {
            var picks = TemplateRotation.Pick(library, WeekAfter(weeks), Facts);
            Assert.DoesNotContain("cold liver", Texts(picks));
            Assert.DoesNotContain(picks.Prints, print => print.TemplateId == "tpl-weekend-lunch-bad");
        }
    }

    [Fact]
    public void NothingApprovedAndNothingProposedIsNoneYet()
    {
        var picks = TemplateRotation.Pick(TemplateLibrary.Empty, Week, Facts);

        Assert.Equal(TemplatePickStatus.NoneYet, picks.Status);
        Assert.Null(picks.Daily);
        Assert.Empty(picks.Prints);
        Assert.Equal(Enum.GetValues<MealTemplateSlot>().Length, picks.Missing.Count);
    }

    [Fact]
    public void OnlyRejectedTemplatesIsStillNoneYet()
    {
        var picks = TemplateRotation.Pick(Lib(Rejected("tpl-weekend-lunch-bad", MealTemplateSlot.WeekendLunch, "cold liver")), Week, Facts);

        Assert.Equal(TemplatePickStatus.NoneYet, picks.Status);
        Assert.Null(picks.Daily);
    }

    [Fact]
    public void ASlotWithNothingLeavesThePagesOutAndNamesIt()
    {
        var library = new TemplateLibrary(Full().Templates.Where(template => template.Slot != MealTemplateSlot.BoxSnack).ToList(), []);

        var picks = TemplateRotation.Pick(library, Week, Facts);

        Assert.Equal(TemplatePickStatus.Incomplete, picks.Status);
        Assert.Null(picks.Daily);
        Assert.Equal([MealTemplateSlot.BoxSnack], picks.Missing);
        Assert.Empty(picks.Prints);
    }

    [Fact]
    public void WeekendLinesAreSeparateFromWeekdayLines()
    {
        var library = new TemplateLibrary(
            [.. Full().Templates.Where(template => template.Slot is not MealTemplateSlot.WeekendBreakfast), Approved("tpl-sat-pancakes", MealTemplateSlot.WeekendBreakfast, "Saturday pancakes")],
            []);

        for (var weeks = 0; weeks < 12; weeks++)
        {
            var picks = TemplateRotation.Pick(library, WeekAfter(weeks), Facts);
            Assert.Equal("Saturday pancakes", picks.Daily!.WeekendBreakfast);
            Assert.DoesNotContain("Saturday pancakes", picks.Daily.WeekdayBreakfasts.Select(line => line.Text));
        }
    }

    [Fact]
    public void ALunchboxIsFiveRotatedParts()
    {
        var box = TemplateRotation.Pick(Full(), Week, Facts).Daily!.Lunchbox;

        Assert.Contains("box-sandwich", box.Sandwich);
        Assert.Contains("box-fruit", box.Fruit);
        Assert.Contains("box-snack", box.Snack);
        Assert.Contains("snack-fruit", box.SeparateFruitOrVeg);
        Assert.Contains("snack-bar", box.SeparateBar);
    }

    [Fact]
    public void WeekdayLinesKeepTheirLabelsAndTakeDifferentTemplates()
    {
        var daily = TemplateRotation.Pick(Full(), Week, Facts).Daily!;

        Assert.Equal(["Sam", "Kids"], daily.WeekdayBreakfasts.Select(line => line.Label));
        Assert.Equal(["Sam", "Kids"], daily.WeekdayLunches.Select(line => line.Label));
        Assert.NotEqual(daily.WeekdayBreakfasts[0].Text, daily.WeekdayBreakfasts[1].Text);
    }

    [Fact]
    public void EveryPrintNamesTheLineItWasPrintedOn()
    {
        var picks = TemplateRotation.Pick(Full(), Week, Facts);

        Assert.Equal(
            ["box-fruit-yes", "box-sandwich-yes", "box-snack-yes", "breakfast-1-yes", "breakfast-2-yes", "lunch-1-yes", "lunch-2-yes", "snack-bar-yes", "snack-fruit-yes", "weekend-breakfast-yes", "weekend-lunch-yes"],
            picks.Prints.Select(print => print.ItemId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(picks.Prints, print => Assert.Equal(Week, print.Week));
    }

    [Fact]
    public void ThePicksAreExactlyTheLinesTheSheetAsksAbout()
    {
        var picks = TemplateRotation.Pick(Full(), Week, Facts);
        var model = Sheet(picks);

        var asked = Constitution.Domain.Meals.MealAnswerRecords.ExpectedItems(model).Where(id => !id.StartsWith("dinner", StringComparison.Ordinal)).ToList();
        foreach (var print in picks.Prints)
        {
            Assert.Contains(print.ItemId, asked);
        }
    }

    internal static Constitution.Domain.Documents.MealSheetModel Sheet(TemplatePicks picks) =>
        Constitution.Domain.Meals.Pipeline.MealSheetBuilder.Build(BuilderTests.Request(picks.Daily));

    [Fact]
    public void ABadWeekOrLabelListIsRefused()
    {
        Assert.Throws<ArgumentException>(() => TemplateRotation.Pick(Full(), Week.AddDays(1), Facts));
        Assert.Throws<ArgumentException>(() => TemplateRotation.Pick(Full(), Week, new TemplateRotationFacts([], ["Sam"])));
        Assert.Throws<ArgumentException>(() => TemplateRotation.Pick(Full(), Week, new TemplateRotationFacts(["a", "b", "c", "d", "e", "f"], ["Sam"])));
    }

    [Fact]
    public void AProposalIsPrintedOnceInItsSlotsFirstLine()
    {
        var library = new TemplateLibrary([.. Full().Templates, Proposed("tpl-weekend-breakfast-new", MealTemplateSlot.WeekendBreakfast, "Fruit salad and toast")], []);

        var picks = TemplateRotation.Pick(library, Week, Facts);

        Assert.Equal("Fruit salad and toast", picks.Daily!.WeekendBreakfast);
        Assert.Contains(new TemplatePrint(Week, "weekend-breakfast-yes", "tpl-weekend-breakfast-new"), picks.Prints);
    }

    [Fact]
    public void ARepickOfTheSameWeekStillShowsTheProposal()
    {
        var library = new TemplateLibrary([.. Full().Templates, Proposed("tpl-weekend-breakfast-new", MealTemplateSlot.WeekendBreakfast, "Fruit salad and toast")], []);
        var picks = TemplateRotation.Pick(library, Week, Facts);

        var recorded = library.WithPrints(picks.Prints);
        var revised = TemplateRotation.Pick(recorded, Week, Facts);

        Assert.Equal("Fruit salad and toast", revised.Daily!.WeekendBreakfast);
    }

    [Fact]
    public void AProposalAlreadyPrintedInAnEarlierWeekIsNotPrintedAgain()
    {
        var library = new TemplateLibrary([.. Full().Templates, Proposed("tpl-weekend-breakfast-new", MealTemplateSlot.WeekendBreakfast, "Fruit salad and toast")], []);
        var recorded = library.WithPrints(TemplateRotation.Pick(library, Week, Facts).Prints);

        for (var weeks = 1; weeks < 8; weeks++)
        {
            var later = TemplateRotation.Pick(recorded, WeekAfter(weeks), Facts);
            Assert.DoesNotContain("Fruit salad and toast", Texts(later));
        }
    }

    [Fact]
    public void AProposalIsNeverInTheRotationOfApprovedTemplates()
    {
        var library = new TemplateLibrary([.. Full().Templates, Proposed("tpl-box-fruit-new", MealTemplateSlot.BoxFruit, "Pear slices")], []);
        var recorded = library.WithPrints([new TemplatePrint(WeekAfter(-3), "box-fruit-yes", "tpl-box-fruit-new")]);

        for (var weeks = 0; weeks < 20; weeks++)
        {
            Assert.DoesNotContain("Pear slices", Texts(TemplateRotation.Pick(recorded, WeekAfter(weeks), Facts)));
        }
    }

    [Fact]
    public void AProposalFillsOnlyTheFirstLineOfAWeekdaySlot()
    {
        var onlyProposals = new TemplateLibrary(
            Enum.GetValues<MealTemplateSlot>().Select(slot => Proposed($"tpl-p-{MealTemplateWords.Slots.Word(slot)}", slot, $"try {MealTemplateWords.Slots.Word(slot)}")).ToList(),
            []);

        var picks = TemplateRotation.Pick(onlyProposals, Week, Facts);

        Assert.Equal(TemplatePickStatus.Incomplete, picks.Status);
        Assert.Equal([MealTemplateSlot.WeekdayBreakfast, MealTemplateSlot.WeekdayLunch], picks.Missing);
        Assert.Empty(picks.Prints);
    }

    [Fact]
    public void ThePlanningWeekNumberDrivesTheRotation()
    {
        var a = TemplateRotation.Pick(Full(), Week, Facts);
        var b = TemplateRotation.Pick(Full(), WeekAfter(3), Facts);

        // Three approved per slot: three weeks on, the rotation is back where it started.
        Assert.Equal(PlanningWeek.IndexOf(Week) + 3, PlanningWeek.IndexOf(WeekAfter(3)));
        Assert.Equal(Texts(a), Texts(b));
    }
}
