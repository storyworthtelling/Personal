using Constitution.Domain.Delivery;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.ReadBack;
using static Constitution.Tests.MealAnswers.MealAnswerData;

namespace Constitution.Tests.MealAnswers;

/// <summary>What a meal answer is, and how the read-back's resolutions become one.</summary>
public class MealAnswerModelTests
{
    [Fact]
    public void ABlankItemIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Tick(Sam, " ", Before(60)));
    }

    [Fact]
    public void ATargetIdIsFrozenLowerCase()
    {
        // The library lower-cases keys; an id with capitals would be two spellings of one record.
        Assert.Throws<ArgumentException>(() => Tick(Sam, "Dinner-Tue-Yes", Before(60)));
    }

    [Fact]
    public void AWeekMustOpenOnASunday()
    {
        Assert.Throws<ArgumentException>(() => Tick(Sam, Tuesday, Before(60), new DateOnly(2026, 10, 26)));
    }

    [Fact]
    public void OnlyANoCarriesReasonWords()
    {
        Assert.Throws<ArgumentException>(() => new MealAnswer(Sam, Week, Tuesday, MealVerdict.Approved, "fine", Before(60)));
        Assert.Throws<ArgumentException>(() => new MealAnswer(Sam, Week, Tuesday, MealVerdict.Unchanged, "fine", Before(60)));
    }

    [Fact]
    public void AReasonIsTrimmedAndBlankIsNone()
    {
        Assert.Equal("we had pasta Monday", Deny(Sam, Tuesday, "  we had pasta Monday ", Before(60)).Reason);
        Assert.Null(Deny(Sam, Tuesday, "   ", Before(60)).Reason);
    }

    [Fact]
    public void ATickBecomesApproved()
    {
        var answer = MealAnswerInk.From(new InkResolution.Ticked(Tuesday), Sam, Week, Before(60));
        Assert.Equal(MealVerdict.Approved, answer!.Verdict);
        Assert.Equal(Tuesday, answer.ItemId);
    }

    [Fact]
    public void AReasonKeepsItsWords()
    {
        var answer = MealAnswerInk.From(new InkResolution.Denied(Tuesday, "Robin has swim"), Lee, Week, Before(60));
        Assert.Equal(MealVerdict.Denied, answer!.Verdict);
        Assert.Equal("Robin has swim", answer.Reason);
    }

    [Fact]
    public void AMarkWithNoWordsStillDenies()
    {
        var answer = MealAnswerInk.From(new InkResolution.Denied(Tuesday, null), Lee, Week, Before(60));
        Assert.Equal(MealVerdict.Denied, answer!.Verdict);
        Assert.Null(answer.Reason);
    }

    [Fact]
    public void SilenceIsUnchangedBeforeOrAfter()
    {
        Assert.Equal(MealVerdict.Unchanged, MealAnswerInk.From(new InkResolution.Undecided(Tuesday), Sam, Week, Before(30))!.Verdict);
        Assert.Equal(MealVerdict.Unchanged, MealAnswerInk.From(new InkResolution.ApprovedAtDeadline(Tuesday), Sam, Week, After(5))!.Verdict);
    }

    [Fact]
    public void AQuestionOrAnUnreadLineIsNoAnswer()
    {
        // Never silence and never a default: a line the read could not settle says nothing at all.
        Assert.Null(MealAnswerInk.From(new InkResolution.NeedsAsking(Tuesday, "Tuesday's dinner", "Keep", "Leave out", "read:x"), Sam, Week, Before(30)));
        Assert.Null(MealAnswerInk.From(new InkResolution.NotRead(Tuesday, "page not read"), Sam, Week, Before(30)));
        Assert.Null(MealAnswerInk.From(new InkResolution.Skipped(Tuesday), Sam, Week, Before(30)));
    }

    [Fact]
    public void AReadResultGivesOneAnswerPerLine()
    {
        var result = new DocumentResult(
            "Constitution — Meals — 2026-10-22",
            DeliveryKind.Meals,
            new DateOnly(2026, 10, 22),
            "Sam",
            Before(45),
            [
                new PageResolutions(1, [new InkResolution.Ticked(Tuesday), new InkResolution.Denied(Wednesday, "out that night"), new InkResolution.Undecided(Thursday)]),
                new PageResolutions(2, [new InkResolution.NotRead("breakfast-1-yes", "not read")]),
            ]);

        var answers = MealAnswerInk.FromResult(result, Sam, Week);

        Assert.Equal([Tuesday, Wednesday, Thursday], answers.Select(answer => answer.ItemId));
        Assert.All(answers, answer => Assert.Equal(Before(45), answer.ReadAt));
        Assert.All(answers, answer => Assert.Equal(Sam, answer.Person));
    }

    [Fact]
    public void OnlyAMealSheetHasAnswers()
    {
        var result = new DocumentResult("x", DeliveryKind.Workout, new DateOnly(2026, 10, 22), "Sam", Before(45), [new PageResolutions(1, [new InkResolution.Ticked("done")])]);
        Assert.Throws<ArgumentException>(() => MealAnswerInk.FromResult(result, Sam, Week));
    }
}
