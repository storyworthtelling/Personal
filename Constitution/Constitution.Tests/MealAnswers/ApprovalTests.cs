using Constitution.Domain.Meals;
using static Constitution.Tests.MealAnswers.MealAnswerData;

namespace Constitution.Tests.MealAnswers;

/// <summary>
/// C-2.4.5: either tick approves, either denial wins, an untouched line is approved when the deadline passes. The answers are
/// what the two tablets said; this is the verdict the planner reads.
/// </summary>
public class ApprovalTests
{
    [Fact]
    public void EitherTickApproves()
    {
        var bySam = Only(Decide(After(1), Tick(Sam, Tuesday, Before(30)), Silent(Lee, Tuesday, Before(30))));
        var byLee = Only(Decide(After(1), Silent(Sam, Tuesday, Before(30)), Tick(Lee, Tuesday, Before(30))));

        Assert.Equal(MealItemOutcome.Approved, bySam.Outcome);
        Assert.Equal(MealApprovalBasis.Tick, bySam.Basis);
        Assert.Equal(MealItemOutcome.Approved, byLee.Outcome);
        Assert.Equal(MealApprovalBasis.Tick, byLee.Basis);
    }

    [Fact]
    public void ATickWithNoOneElseHeardStillApproves()
    {
        var item = Only(Decide(After(1), Tick(Sam, Tuesday, Before(30))));
        Assert.Equal(MealItemOutcome.Approved, item.Outcome);
    }

    [Fact]
    public void EitherDenialWins()
    {
        var samDenies = Only(Decide(After(1), Deny(Sam, Tuesday, "we had it Monday", Before(30)), Tick(Lee, Tuesday, Before(30))));
        var leeDenies = Only(Decide(After(1), Tick(Sam, Tuesday, Before(30)), Deny(Lee, Tuesday, "Robin has swim", Before(30))));

        Assert.Equal(MealItemOutcome.Denied, samDenies.Outcome);
        Assert.Equal("we had it Monday", Assert.Single(samDenies.Denials).Reason);
        Assert.Equal(MealItemOutcome.Denied, leeDenies.Outcome);
        Assert.Equal("Robin has swim", Assert.Single(leeDenies.Denials).Reason);
    }

    [Fact]
    public void ADenialBeatsSilenceToo()
    {
        var item = Only(Decide(After(1), Silent(Sam, Tuesday, Before(30)), Deny(Lee, Tuesday, "no fish", Before(30))));
        Assert.Equal(MealItemOutcome.Denied, item.Outcome);
    }

    [Fact]
    public void SilenceApprovesAtTheDeadline()
    {
        var answers = new[] { Silent(Sam, Tuesday, Before(45)), Silent(Lee, Tuesday, Before(45)) };

        var atTheDeadline = Only(MealDeadline.Decide(answers, Deadline, Deadline));
        var oneTickBefore = Only(MealDeadline.Decide(answers, Deadline, Deadline - TimeSpan.FromTicks(1)));

        Assert.Equal(MealItemOutcome.Approved, atTheDeadline.Outcome);
        Assert.Equal(MealApprovalBasis.Silence, atTheDeadline.Basis);
        Assert.Equal(MealItemOutcome.Pending, oneTickBefore.Outcome);
    }

    [Fact]
    public void ATickIsNotDecidedBeforeTheDeadline()
    {
        // The other adult may still say no before it passes.
        var item = Only(Decide(Before(10), Tick(Sam, Tuesday, Before(30)), Tick(Lee, Tuesday, Before(30))));
        Assert.Equal(MealItemOutcome.Pending, item.Outcome);
    }

    [Fact]
    public void ADenialIsKnownAtOnce()
    {
        var item = Only(Decide(Before(10), Tick(Sam, Tuesday, Before(30)), Deny(Lee, Tuesday, "no", Before(20))));
        Assert.Equal(MealItemOutcome.Denied, item.Outcome);
    }

    [Fact]
    public void ADenialSendsARevisedSheet()
    {
        var decision = Decide(
            After(1),
            Tick(Sam, Tuesday, Before(30)),
            Tick(Lee, Tuesday, Before(30)),
            Deny(Lee, Wednesday, "out that night", Before(30)),
            Silent(Sam, Wednesday, Before(30)));

        Assert.True(decision.NeedsRevisedSheet);
        var denied = Assert.Single(decision.Denied);
        Assert.Equal(Wednesday, denied.ItemId);
        Assert.Equal([Tuesday], decision.ApprovedItemIds);
    }

    [Fact]
    public void NoDenialNeedsNoRevision()
    {
        var decision = Decide(After(1), Tick(Sam, Tuesday, Before(30)), Silent(Lee, Wednesday, Before(30)));
        Assert.False(decision.NeedsRevisedSheet);
        Assert.Empty(decision.Denied);
    }

    [Fact]
    public void BothReasonsAreKeptInPersonOrder()
    {
        var item = Only(Decide(After(1), Deny(Sam, Tuesday, "we had it Monday", Before(30)), Deny(Lee, Tuesday, null, Before(30))));

        Assert.Equal(["lee", "sam"], item.Denials.Select(denial => denial.Person));
        Assert.Null(item.Denials[0].Reason);
        Assert.Equal("we had it Monday", item.Denials[1].Reason);
    }

    [Fact]
    public void ADenialWithNoWordsStillDenies()
    {
        var item = Only(Decide(After(1), Deny(Lee, Tuesday, null, Before(30)), Tick(Sam, Tuesday, Before(30))));
        Assert.Equal(MealItemOutcome.Denied, item.Outcome);
        Assert.Null(Assert.Single(item.Denials).Reason);
    }

    [Fact]
    public void ALateDenialNeverReversesAnApproval()
    {
        var late = Deny(Lee, Tuesday, "changed my mind", After(60));
        var item = Only(Decide(After(120), Silent(Sam, Tuesday, Before(30)), late));

        Assert.Equal(MealItemOutcome.Approved, item.Outcome);
        Assert.Empty(item.Denials);
        Assert.Equal(late, Assert.Single(item.Ignored));
    }

    [Fact]
    public void ALateDenialLeavesATickApproved()
    {
        var item = Only(Decide(After(120), Tick(Sam, Tuesday, Before(30)), Deny(Lee, Tuesday, "no", After(1))));
        Assert.Equal(MealItemOutcome.Approved, item.Outcome);
        Assert.Equal(MealApprovalBasis.Tick, item.Basis);
    }

    [Fact]
    public void ALateTickNeverReversesADenial()
    {
        var item = Only(Decide(After(120), Deny(Lee, Tuesday, "no", Before(30)), Tick(Sam, Tuesday, After(30))));
        Assert.Equal(MealItemOutcome.Denied, item.Outcome);
    }

    [Fact]
    public void ReadingAtTheDeadlineIsLate()
    {
        var item = Only(Decide(After(5), Silent(Sam, Tuesday, Before(1)), Deny(Lee, Tuesday, "no", Deadline)));
        Assert.Equal(MealItemOutcome.Approved, item.Outcome);
    }

    [Fact]
    public void AReadOneTickBeforeCounts()
    {
        var item = Only(Decide(After(5), Silent(Sam, Tuesday, Before(1)), Deny(Lee, Tuesday, "no", Deadline - TimeSpan.FromTicks(1))));
        Assert.Equal(MealItemOutcome.Denied, item.Outcome);
    }

    [Fact]
    public void NobodyHeardIsNotApproved()
    {
        // Silence approves at a read; a line no tablet was read for has had none.
        var item = Only(MealDeadline.Decide([], Deadline, After(60), [Tuesday]));
        Assert.Equal(MealItemOutcome.Unheard, item.Outcome);
    }

    [Fact]
    public void ALineNobodyHasReadWaitsBeforeTheDeadline()
    {
        var item = Only(MealDeadline.Decide([], Deadline, Before(60), [Tuesday]));
        Assert.Equal(MealItemOutcome.Pending, item.Outcome);
    }

    [Fact]
    public void AnExpectedLineWithNoAnswerJoinsTheOthers()
    {
        var decision = MealDeadline.Decide([Tick(Sam, Tuesday, Before(30))], Deadline, After(1), [Tuesday, Wednesday]);
        Assert.Equal([Tuesday], decision.ApprovedItemIds);
        Assert.Equal(Wednesday, Assert.Single(decision.Unheard).ItemId);
        Assert.False(decision.IsSettled);
    }

    [Fact]
    public void OnlyLateReadingsStillDecide()
    {
        var silent = Only(Decide(After(120), Silent(Sam, Tuesday, After(30)), Silent(Lee, Tuesday, After(40))));
        var denied = Only(Decide(After(120), Silent(Sam, Tuesday, After(30)), Deny(Lee, Tuesday, "no", After(40))));

        Assert.Equal(MealItemOutcome.Approved, silent.Outcome);
        Assert.Equal(MealApprovalBasis.Silence, silent.Basis);
        Assert.Equal(MealItemOutcome.Denied, denied.Outcome);
    }

    [Fact]
    public void ALatestOnTimeReadingSupersedesAnEarlier()
    {
        var changedToNo = Only(Decide(After(1), Tick(Lee, Tuesday, Before(90)), Deny(Lee, Tuesday, "no", Before(20))));
        var changedToYes = Only(Decide(After(1), Deny(Lee, Tuesday, "no", Before(90)), Tick(Lee, Tuesday, Before(20))));

        Assert.Equal(MealItemOutcome.Denied, changedToNo.Outcome);
        Assert.Equal(MealItemOutcome.Approved, changedToYes.Outcome);
    }

    [Fact]
    public void AdultsLinesAreJudgedAlone()
    {
        var decision = MealDeadline.Decide(
            [Deny(Sam, Tuesday, "no", Before(30)), Tick(Lee, Wednesday, Before(30)), Silent(Sam, Thursday, Before(30))],
            Deadline,
            After(1),
            [Tuesday, Wednesday, Thursday]);

        Assert.Equal(MealItemOutcome.Denied, decision.Item(Tuesday)!.Outcome);
        Assert.Equal(MealItemOutcome.Approved, decision.Item(Wednesday)!.Outcome);
        Assert.Equal(MealItemOutcome.Approved, decision.Item(Thursday)!.Outcome);
        Assert.Equal([Wednesday, Thursday], decision.ApprovedItemIds);
        Assert.Null(decision.Item("dinner-fri-yes"));
    }

    [Fact]
    public void TwoWeeksAreNeverMixed()
    {
        var other = new DateOnly(2026, 11, 1);
        Assert.Throws<ArgumentException>(() => Decide(After(1), Tick(Sam, Tuesday, Before(30)), Tick(Lee, Tuesday, Before(30), other)));
    }

    [Fact]
    public void TheDecisionIsFinalFromTheDeadline()
    {
        Assert.False(Decide(Before(1), Tick(Sam, Tuesday, Before(30))).IsFinal);
        Assert.True(Decide(Deadline, Tick(Sam, Tuesday, Before(30))).IsFinal);
    }

    [Fact]
    public void ASheetWithNothingPendingOrUnheardIsSettled()
    {
        Assert.True(Decide(After(1), Tick(Sam, Tuesday, Before(30)), Deny(Lee, Wednesday, "no", Before(30))).IsSettled);
        Assert.False(Decide(Before(1), Tick(Sam, Tuesday, Before(30))).IsSettled);
    }

    [Fact]
    public void NoAnswersAtAllIsAnEmptyDecision()
    {
        var decision = MealDeadline.Decide([], Deadline, After(1));
        Assert.Empty(decision.Items);
        Assert.Null(decision.Week);
        Assert.False(decision.NeedsRevisedSheet);
    }

    [Fact]
    public void TheOrderOfAnswersChangesNothing()
    {
        var answers = new[]
        {
            Tick(Sam, Tuesday, Before(30)),
            Silent(Lee, Tuesday, Before(40)),
            Deny(Lee, Wednesday, "no", Before(30)),
            Silent(Sam, Wednesday, Before(50)),
            Tick(Lee, Thursday, After(10)),
            Deny(Sam, Thursday, "late no", After(20)),
        };
        var expected = Summary(MealDeadline.Decide(answers, Deadline, After(60)));

        var random = new Random(7);
        for (var round = 0; round < 25; round++)
        {
            var shuffled = answers.OrderBy(_ => random.Next()).ToArray();
            Assert.Equal(expected, Summary(MealDeadline.Decide(shuffled, Deadline, After(60))));
        }
    }

    private static string Summary(MealDecision decision) =>
        string.Join(";", decision.Items.Select(item => $"{item.ItemId}={item.Outcome}/{item.Basis}/{string.Join(",", item.Denials.Select(d => d.Person + ":" + d.Reason))}"));
}
