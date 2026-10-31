using Constitution.Domain.Groceries;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using Constitution.Tests.MealDeadlineStep;
using Constitution.Tests.MealWeekDocuments;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Groceries;

/// <summary>D112: the Meals tab's share builds the list from the kept week, the pantry, the headcount and the decision record, then aims it at Keep alone. Invented data.</summary>
public class KeepShareRunTests
{
    private readonly FakePantry pantry = new();
    private readonly FakeRoster roster = new();
    private readonly MemoryDecisions decisions = new();

    private KeepSharePrepared Prepare(MealWeek? week = null) =>
        new KeepShareRun(pantry, roster, decisions).Prepare(week ?? DocsData.Week(), DocsData.Cards());

    private static KeepSharePrepared.Ready Ready(KeepSharePrepared prepared) => Assert.IsType<KeepSharePrepared.Ready>(prepared);

    private static KeepShareReport Settled(KeepSharePrepared prepared) => Assert.IsType<KeepSharePrepared.Settled>(prepared).Report;

    private void Denied(params string[] evenings) =>
        decisions.Save(MealDecisionRecord.Empty.With(new MealDecisionEntry(Sunday, Edt(10, 25, 15), 5, evenings.Length, 0, false) { DeniedEvenings = evenings }));

    [Fact]
    public void TheListIsTheKeptWeeksAisles()
    {
        var expected = GroceryList.From(DocsData.Week(), DocsData.Cards(), DocsData.Stocked(), DocsData.People().Count);

        var ready = Ready(Prepare());

        Assert.Equal(GroceryShare.Plan(expected, true).Body, ready.Decision.Body);
        Assert.Equal(GroceryShareOutcome.Send, ready.Decision.Outcome);
    }

    [Fact]
    public void ItIsAimedAtKeepAlone()
    {
        Assert.Equal(GroceryShare.KeepPackage, Ready(Prepare()).Decision.Package);
    }

    [Fact]
    public void ADeniedEveningIsAnAlsoLine()
    {
        Denied("tuesday", "friday");

        var body = Ready(Prepare()).Decision.Body;

        Assert.EndsWith("Also\nTuesday dinner\nFriday dinner", body, StringComparison.Ordinal);
    }

    [Fact]
    public void NoDenialMeansNoAlsoBlock()
    {
        Assert.DoesNotContain("Also", Ready(Prepare()).Decision.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherWeeksDenialIsNotThisWeeks()
    {
        decisions.Save(MealDecisionRecord.Empty.With(new MealDecisionEntry(Sunday.AddDays(-7), Edt(10, 18, 15), 5, 1, 0, false) { DeniedEvenings = ["tuesday"] }));

        Assert.DoesNotContain("Tuesday dinner", Ready(Prepare()).Decision.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadcountScalesTheQuantities()
    {
        var four = Ready(Prepare()).Decision.Body;
        roster.Answer = new MealRosterRead.Ready(DocsData.People().Take(2).ToList());

        var two = Ready(Prepare()).Decision.Body;

        Assert.NotEqual(four, two);
    }

    [Fact]
    public void ThePantryTakesItemsOff()
    {
        var stocked = Ready(Prepare()).Decision.Body;
        pantry.Answer = new MealPantryRead.NoneYet("There is no household file yet.");

        var bare = Ready(Prepare()).Decision.Body;

        Assert.NotEqual(stocked, bare);
        Assert.Equal(GroceryShare.Plan(GroceryList.From(DocsData.Week(), DocsData.Cards(), Pantry.Empty, 4), true).Body, bare);
    }

    [Fact]
    public void AnUnreadableRosterSettlesWithItsReason()
    {
        roster.Answer = new MealRosterRead.Unavailable("The household file cannot be opened.");

        var report = Settled(Prepare());

        Assert.Equal(KeepShareOutcome.ListNotBuilt, report.Outcome);
        Assert.StartsWith("The household file cannot be opened.", report.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyRosterSettlesRatherThanThrowing()
    {
        roster.Answer = new MealRosterRead.Ready([]);

        Assert.Equal(KeepShareOutcome.ListNotBuilt, Settled(Prepare()).Outcome);
    }

    [Fact]
    public void AnUnreadablePantrySettlesAndNeverReadsAsEmpty()
    {
        pantry.Answer = new MealPantryRead.Unreadable("The pantry cannot be read.");

        var report = Settled(Prepare());

        Assert.Equal(KeepShareOutcome.ListNotBuilt, report.Outcome);
        Assert.StartsWith("The pantry cannot be read.", report.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ADinnerWhoseCardIsGoneSettlesWithTheFailureKept()
    {
        var report = Settled(new KeepShareRun(pantry, roster, decisions).Prepare(DocsData.Week(), []));

        Assert.Equal(KeepShareOutcome.ListNotBuilt, report.Outcome);
        Assert.IsType<ArgumentException>(report.Failure);
    }

    [Fact]
    public void AnUnreadableRecordStillSharesAndSaysWhatIsMissing()
    {
        decisions.Json = "{ not json";

        var ready = Ready(Prepare());

        Assert.False(string.IsNullOrWhiteSpace(ready.Caveat));
        Assert.DoesNotContain("Also", ready.Decision.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadableRecordHasNoCaveat()
    {
        Denied("tuesday");

        Assert.Null(Ready(Prepare()).Caveat);
    }

    [Fact]
    public void AWeekWithNothingOnItHasNothingToSend()
    {
        pantry.Answer = new MealPantryRead.NoneYet("There is no household file yet.");
        var report = Settled(Prepare(MealPlacement.MealPlacementData.WeekOf()));

        Assert.Equal(KeepShareOutcome.NothingToSend, report.Outcome);
        Assert.Equal("There is nothing on the list to send.", report.Sentence);
    }

    [Fact]
    public void ADeniedEveningAloneIsStillSent()
    {
        pantry.Answer = new MealPantryRead.NoneYet("There is no household file yet.");
        Denied("tuesday");

        var ready = Ready(Prepare(MealPlacement.MealPlacementData.WeekOf()));

        Assert.Equal("Also\nTuesday dinner", ready.Decision.Body);
    }

    [Fact]
    public void AStartedIntentIsSent()
    {
        var report = KeepShareRun.Finish(Ready(Prepare()), KeepStart.Started);

        Assert.Equal(KeepShareOutcome.Sent, report.Outcome);
        Assert.Equal("The list went to Keep.", report.Sentence);
    }

    [Fact]
    public void ASentListSaysWhatWasLeftOffIt()
    {
        decisions.Json = "{ not json";

        var report = KeepShareRun.Finish(Ready(Prepare()), KeepStart.Started);

        Assert.StartsWith("The list went to Keep.", report.Sentence, StringComparison.Ordinal);
        Assert.Contains("could not be read", report.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void NoKeepIsTheKnownSentence()
    {
        var report = KeepShareRun.Finish(Ready(Prepare()), KeepStart.NotFound);

        Assert.Equal(KeepShareOutcome.KeepNotInstalled, report.Outcome);
        Assert.Equal("Keep is not on this phone, so the list was not sent.", report.Sentence);
    }

    [Fact]
    public void ARefusingKeepIsSaidPlainly()
    {
        var report = KeepShareRun.Finish(Ready(Prepare()), KeepStart.Refused);

        Assert.Equal(KeepShareOutcome.KeepRefused, report.Outcome);
        Assert.Equal("Keep would not take the list, so it was not sent.", report.Sentence);
    }

    [Fact]
    public void TheActionIsCalledWhatTheBarSays()
    {
        Assert.Equal("Share the list", KeepShareRun.ActionLabel);
    }
}
