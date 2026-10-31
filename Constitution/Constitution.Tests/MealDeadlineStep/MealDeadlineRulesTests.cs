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

    // ---- what is ahead (D111) ----

    [Fact]
    public void OnlyDeniedDinnersFromTodayOnAreDeniedEveningsAhead()
    {
        var stored = Stored();
        var first = Cooked(stored, 0);
        var second = Cooked(stored, 1);
        var decision = Decision(After(60), Deny(Sam, first.Line, "no", Before(30)), Deny(Lee, second.Line, "no", Before(30)));

        var ahead = MealDeadlineRules.DeniedEveningsAhead(decision, stored, second.Day);

        Assert.Equal(second.Day, Assert.Single(ahead).Day);
        Assert.Equal(2, MealDeadlineRules.DeniedEveningsAhead(decision, stored, first.Day).Count);
        Assert.Empty(MealDeadlineRules.DeniedEveningsAhead(decision, stored, Family.Sunday.AddDays(7)));
    }

    [Fact]
    public void ADeniedEatOutEveningIsAnEveningAheadToo()
    {
        var stored = Stored();
        var leftovers = stored.Week.Dinners.First(item => item.Kind != DinnerKind.Cooked);
        var decision = Decision(After(60), Deny(Sam, MealSheetIds.DinnerYes(leftovers.Day.DayOfWeek), "no", Before(30)));

        Assert.Equal(leftovers.Day, Assert.Single(MealDeadlineRules.DeniedEveningsAhead(decision, stored, Family.Sunday)).Day);
    }

    [Fact]
    public void ARefusedTemplateLineIsNoEvening()
    {
        var stored = Stored();
        var library = TemplateData.Full().WithPrints([new TemplatePrint(Family.Sunday, "box-fruit-yes", "tpl-box-fruit-oats")]);
        var decision = MealDeadline.Decide(
            [Deny(Lee, "box-fruit-yes", "no", Before(30))], Deadline, After(60), MealDeadlineRules.ExpectedLines(stored, library));

        Assert.Empty(MealDeadlineRules.DeniedEveningsAhead(decision, stored, Family.Sunday));
    }

    [Fact]
    public void NothingDeniedLeavesNoEvening()
    {
        Assert.Empty(MealDeadlineRules.DeniedEveningsAhead(Decision(After(60)), Stored(), Family.Sunday));
    }

    [Fact]
    public void LeavingAnEveningUnplannedChangesThatEveningAndNothingElse()
    {
        var stored = Stored();
        var denied = Cooked(stored, 1);

        var after = MealDeadlineRules.LeaveUnplanned(stored, [denied.Day]);

        var left = after.Week.Dinners.Single(item => item.Day == denied.Day);
        Assert.Equal(DinnerKind.NothingPlanned, left.Kind);
        Assert.Equal(DeniedEveningWords.NotApprovedReason, left.Reason);
        Assert.Null(left.Recipe);
        Assert.Null(left.Cook);
        foreach (var dinner in stored.Week.Dinners.Where(item => item.Day != denied.Day))
        {
            Assert.Equal(dinner, after.Week.Dinners.Single(item => item.Day == dinner.Day));
        }

        Assert.Equal(stored.PlannedAt, after.PlannedAt);
        Assert.Equal(stored.Week.Notes, after.Week.Notes);
        Assert.Equal(stored.WeekStart, after.WeekStart);
    }

    [Fact]
    public void ADishNoOtherEveningNamesLeavesTheWeeksDishes()
    {
        var stored = Stored() with { Dishes = Stored().Week.Dinners.Where(item => item.Kind == DinnerKind.Cooked).Select(item => new StoredDish(item.Recipe!.Value, "A card")).DistinctBy(dish => dish.Recipe).ToList() };
        var denied = Cooked(stored, 0);
        var otherEveningsNameIt = stored.Week.Dinners.Count(item => item.Recipe == denied.Card) > 1;

        var after = MealDeadlineRules.LeaveUnplanned(stored, [denied.Day]);

        Assert.Equal(otherEveningsNameIt, after.Dishes.Any(dish => dish.Recipe == denied.Card));
        Assert.Equal(stored.Dishes.Count - (otherEveningsNameIt ? 0 : 1), after.Dishes.Count);
    }

    [Fact]
    public void AnEveningAlreadyUnplannedIsLeftExactlyAsItWas()
    {
        var stored = Stored();
        var denied = Cooked(stored, 0);
        var once = MealDeadlineRules.LeaveUnplanned(stored, [denied.Day]);

        var twice = MealDeadlineRules.LeaveUnplanned(once, [denied.Day]);

        Assert.Equal(MealWeekRecordCodec.Encode(new MealWeekRecord([once])), MealWeekRecordCodec.Encode(new MealWeekRecord([twice])));
    }

    [Fact]
    public void TheWeekdayWordsAreFrozenAndTheLineIsPlain()
    {
        Assert.Equal(["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"], Enum.GetValues<DayOfWeek>().OrderBy(day => (int)day).Select(DeniedEveningWords.Of));
        Assert.Equal("Tuesday dinner", DeniedEveningWords.Line("tuesday"));
        Assert.Equal(["Friday dinner", "Sunday dinner"], DeniedEveningWords.Lines(["friday", "someday", "friday", "sunday"]));
        Assert.Throws<ArgumentException>(() => DeniedEveningWords.Line("someday"));
    }
}
