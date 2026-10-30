using Constitution.Domain.Budgets;
using Constitution.Domain.Documents;
using Constitution.Domain.Groceries;
using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.Recipes;
using Constitution.Tests.Templates;
using Family = Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealAnswers.MealAnswerData;
using static Constitution.Tests.MealWeekDocuments.DocsData;

namespace Constitution.Tests.MealDeadlineStep;

/// <summary>The deadline step's pure rules over an invented week.</summary>
public class MealDeadlineRulesTests
{
    private static StoredMealWeek Stored() => new(Week(), Family.Edt(10, 21, 12), []);

    private static MealDecision Decision(DateTimeOffset now, params MealAnswer[] answers) =>
        MealDeadline.Decide(answers, Deadline, now, MealDeadlineRules.ExpectedLines(Stored(), null));

    private static (string Line, RecipeId Card, DateOnly Day) Cooked(StoredMealWeek stored, int skip = 0)
    {
        var dinner = stored.Week.Dinners.Where(item => item.Kind == DinnerKind.Cooked).Skip(skip).First();
        return (MealSheetIds.DinnerYes(dinner.Day.DayOfWeek), dinner.Recipe!.Value, dinner.Day);
    }

    // ---- the lines ----

    [Fact]
    public void TheExpectedLinesAreExactlyTheLinesOfTheBuiltSheet()
    {
        var library = TemplateData.Full();
        var picks = TemplateRotation.Pick(library, Family.Sunday, new TemplateRotationFacts(["Sam", "Lee"], ["Sam", "Lee"]));
        Assert.Equal(TemplatePickStatus.Ready, picks.Status);
        var model = MealSheetBuilder.Build(new MealSheetBuildRequest(Week(), Cards(), Family.Zone, 4, Stocked(), MonthlyBudgets.None, new UnavailableHolidayCalendar())
        {
            Daily = picks.Daily,
        });

        var fromTheSheet = MealAnswerRecords.ExpectedItems(model);
        var mine = MealDeadlineRules.ExpectedLines(Stored(), library.WithPrints(picks.Prints));

        Assert.Equal(fromTheSheet.Order(StringComparer.Ordinal), mine.Order(StringComparer.Ordinal));
        Assert.Equal(7 + picks.Prints.Count, mine.Count);
    }

    [Fact]
    public void WithoutALibraryTheLinesAreTheSevenDinners()
    {
        var lines = MealDeadlineRules.ExpectedLines(Stored(), null);

        Assert.Equal(7, lines.Count);
        Assert.StartsWith("dinner-sun-yes", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyThisWeeksPrintsAreLines()
    {
        var library = TemplateData.Full().WithPrints([new TemplatePrint(Family.Sunday, "box-fruit-yes", "tpl-box-fruit-oats"), new TemplatePrint(Family.Sunday.AddDays(-7), "box-snack-yes", "tpl-box-snack-oats")]);

        var lines = MealDeadlineRules.ExpectedLines(Stored(), library);

        Assert.Contains("box-fruit-yes", lines);
        Assert.DoesNotContain("box-snack-yes", lines);
    }

    // ---- evidence ----

    [Fact]
    public void ADenialsWordsBecomeEvidenceForTheDinnerItNames()
    {
        var stored = Stored();
        var (line, card, _) = Cooked(stored, 1);
        var decision = Decision(After(60), Deny(Sam, line, "  too rich  ", Before(30)), Deny(Lee, line, null, Before(20)));

        var evidence = MealDeadlineRules.DinnerEvidence(decision, stored, Family.Sunday.AddDays(1));

        var item = Assert.Single(evidence);
        Assert.Equal(new PersonId("sam"), item.Person);
        Assert.Equal(TasteEvidenceKind.Denial, item.Kind);
        Assert.Equal("too rich", item.Words);
        Assert.Equal(card, item.Recipe);
        Assert.Equal(Family.Sunday.AddDays(1), item.Day);
    }

    [Fact]
    public void AVeryLongReasonIsCutToTheEvidenceLimit()
    {
        var stored = Stored();
        var (line, _, _) = Cooked(stored);
        var decision = Decision(After(60), Deny(Sam, line, new string('x', 1500), Before(30)));

        var item = Assert.Single(MealDeadlineRules.DinnerEvidence(decision, stored, Family.Sunday));

        Assert.Equal(TasteEvidence.MaxWordsLength, item.Words.Length);
    }

    [Fact]
    public void TicksAndSilenceAndTemplateLinesAreNotDinnerEvidence()
    {
        var stored = Stored();
        var (line, _, _) = Cooked(stored);
        var decision = Decision(After(60), Tick(Sam, line, Before(30)), Deny(Lee, "box-fruit-yes", "no oats", Before(30)));

        Assert.Empty(MealDeadlineRules.DinnerEvidence(decision, stored, Family.Sunday));
    }

    [Fact]
    public void ALeftoversDinnerDeniedHasEvidenceWithoutACard()
    {
        var stored = Stored();
        var leftovers = stored.Week.Dinners.First(item => item.Kind != DinnerKind.Cooked);
        var line = MealSheetIds.DinnerYes(leftovers.Day.DayOfWeek);
        var decision = Decision(After(60), Deny(Sam, line, "not leftovers again", Before(30)));

        var item = Assert.Single(MealDeadlineRules.DinnerEvidence(decision, stored, Family.Sunday));

        Assert.Null(item.Recipe);
    }

    // ---- what is ahead ----

    [Fact]
    public void OnlyDeniedDinnersFromTodayOnAreAhead()
    {
        var stored = Stored();
        var first = Cooked(stored, 0);
        var second = Cooked(stored, 1);
        var decision = Decision(After(60), Deny(Sam, first.Line, "no", Before(30)), Deny(Lee, second.Line, "no", Before(30)));

        var ahead = MealDeadlineRules.DeniedAhead(decision, stored, second.Day);

        Assert.Equal(second.Card, Assert.Single(ahead));
        Assert.Equal(2, MealDeadlineRules.DeniedAhead(decision, stored, first.Day).Count);
        Assert.Empty(MealDeadlineRules.DeniedAhead(decision, stored, Family.Sunday.AddDays(7)));
    }

    [Fact]
    public void ARefusedLineOfPagesTwoAndThreeNeedsARevisedSheetWhileTheWeekRuns()
    {
        var stored = Stored();
        var library = TemplateData.Full().WithPrints([new TemplatePrint(Family.Sunday, "box-fruit-yes", "tpl-box-fruit-oats")]);
        var decision = MealDeadline.Decide(
            [Deny(Lee, "box-fruit-yes", "no", Before(30))], Deadline, After(60), MealDeadlineRules.ExpectedLines(stored, library));

        Assert.True(MealDeadlineRules.NeedsRevisedSheet(decision, stored, Family.Sunday.AddDays(3)));
        Assert.False(MealDeadlineRules.NeedsRevisedSheet(decision, stored, Family.Sunday.AddDays(7)));
    }

    [Fact]
    public void ADeniedDinnerThatIsOverNeedsNoRevisedSheet()
    {
        var stored = Stored();
        var (line, _, day) = Cooked(stored, 0);
        var decision = Decision(After(60), Deny(Sam, line, "no", Before(30)));

        Assert.True(MealDeadlineRules.NeedsRevisedSheet(decision, stored, day));
        Assert.False(MealDeadlineRules.NeedsRevisedSheet(decision, stored, day.AddDays(1)));
    }

    [Fact]
    public void NothingDeniedNeedsNothing()
    {
        var decision = Decision(After(60));

        Assert.False(MealDeadlineRules.NeedsRevisedSheet(decision, Stored(), Family.Sunday));
        Assert.Empty(MealDeadlineRules.DeniedAhead(decision, Stored(), Family.Sunday));
    }
}
