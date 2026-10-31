using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Workouts;
using Constitution.Tests.MealTaste;
using static Constitution.Tests.MealAnswers.MealAnswerData;
using static Constitution.Tests.MealDeadlineStep.DeadlineRig;
using Family = Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.MealDeadlineStep;

/// <summary>Words the judge could not settle are asked again on later passes, but never without bound and never while there is no key.</summary>
public class JudgeRetryTests
{
    private static TasteJudgement Down(AuthorUnavailable reason) => new TasteJudgement.Unavailable(reason, "stand-in down");

    private static async Task<DeadlineRig> DeniedAsync(Func<TasteEvidence, TasteJudgement> answer)
    {
        var rig = await PlannedAsync();
        rig.Answers.All.Add(Deny(Sam, rig.CookedDinner().Line, "not again", Before(60)));
        rig.Judge = new FakeTasteJudge(answer);
        return rig;
    }

    [Fact]
    public async Task APassThatReachedTheJudgeAndFailedCountsAnAttempt()
    {
        var rig = await DeniedAsync(_ => Down(AuthorUnavailable.Offline));

        await rig.DecidedAsync();
        await rig.DecidedAsync();

        var entry = rig.Decisions.Held().For(Week)!;
        Assert.Equal(2, entry.JudgeAttempts);
        Assert.True(entry.EvidencePending);
    }

    [Fact]
    public async Task AfterTheLastAttemptTheWordsAreLetGoAndNothingIsAskedAgain()
    {
        var rig = await DeniedAsync(_ => Down(AuthorUnavailable.KeyRejected));

        for (var pass = 0; pass < TasteJudgeLimits.MostAttempts; pass++)
        {
            await rig.DecidedAsync();
        }

        var entry = rig.Decisions.Held().For(Week)!;
        Assert.False(entry.EvidencePending);
        Assert.Equal(TasteJudgeLimits.MostAttempts, entry.JudgeAttempts);
        var asked = ((FakeTasteJudge)rig.Judge!).Asked.Count;
        var next = await rig.RunAsync();
        Assert.Equal(MealDeadlineNothingBecause.AlreadyDecided, Assert.IsType<MealDeadlineOutcome.NothingToDecide>(next).Because);
        Assert.Equal(asked, ((FakeTasteJudge)rig.Judge!).Asked.Count);
    }

    [Fact]
    public async Task NoKeyNeverUsesUpAnAttempt()
    {
        var rig = await DeniedAsync(_ => Down(AuthorUnavailable.NotConfigured));

        for (var pass = 0; pass < TasteJudgeLimits.MostAttempts + 3; pass++)
        {
            await rig.DecidedAsync();
        }

        var entry = rig.Decisions.Held().For(Week)!;
        Assert.True(entry.EvidencePending);
        Assert.Equal(0, entry.JudgeAttempts);
    }

    [Fact]
    public async Task AKeyRestoredLaterStillJudgesTheWaitingWords()
    {
        var rig = await DeniedAsync(_ => Down(AuthorUnavailable.NotConfigured));
        var card = rig.CookedDinner().Card;
        await rig.DecidedAsync();
        await rig.DecidedAsync();
        rig.Judge = new FakeTasteJudge(_ => new TasteJudgement.Lasting([new TasteLesson(TasteSubjectKind.Dish, card.Value, TasteStance.Dislikes)]));

        var report = await rig.DecidedAsync();

        Assert.Equal(1, report.Lasting);
        Assert.False(rig.Decisions.Held().For(Week)!.EvidencePending);
        Assert.Single(rig.Notes.Kept);
    }

    [Fact]
    public async Task APassAsksAtMostItsLimitOfQuestions()
    {
        var rig = await PlannedAsync();
        for (var skip = 0; skip < 4; skip++)
        {
            var line = rig.CookedDinner(skip).Line;
            rig.Answers.All.Add(Deny(Sam, line, "not again " + skip, Before(60)));
            rig.Answers.All.Add(Deny(Lee, line, "too plain " + skip, Before(59)));
        }

        var judge = new FakeTasteJudge(_ => new TasteJudgement.OneOff());
        rig.Judge = judge;

        var report = await rig.DecidedAsync();

        Assert.Equal(TasteJudgeLimits.MostPerPass, judge.Asked.Count);
        Assert.Equal(8 - TasteJudgeLimits.MostPerPass, report.Unjudged);
        Assert.True(rig.Decisions.Held().For(Week)!.EvidencePending);
    }

    [Fact]
    public async Task AFailingServiceIsAskedOnceAPassNotOncePerWord()
    {
        var rig = await PlannedAsync();
        for (var skip = 0; skip < 3; skip++)
        {
            rig.Answers.All.Add(Deny(Sam, rig.CookedDinner(skip).Line, "not again " + skip, Before(60)));
        }

        var judge = new FakeTasteJudge(_ => Down(AuthorUnavailable.Offline));
        rig.Judge = judge;

        var report = await rig.DecidedAsync();

        Assert.Single(judge.Asked);
        Assert.Equal(3, report.Unjudged);
    }

    [Fact]
    public async Task AnUnusableReplyToOneWordStillLetsTheOthersBeJudged()
    {
        var rig = await PlannedAsync();
        rig.Answers.All.Add(Deny(Sam, rig.CookedDinner(0).Line, "first words", Before(60)));
        rig.Answers.All.Add(Deny(Sam, rig.CookedDinner(1).Line, "second words", Before(59)));
        rig.Judge = new FakeTasteJudge(evidence => evidence.Words == "first words" ? Down(AuthorUnavailable.ReplyUnusable) : new TasteJudgement.OneOff());

        var report = await rig.DecidedAsync();

        Assert.Equal(1, report.Unjudged);
        Assert.Equal(1, report.OneOff);
    }

    // ---- the record keeps the count ----

    [Fact]
    public void TheAttemptsComeBackFromTheFile()
    {
        var entry = new MealDecisionEntry(Family.Sunday, Family.Edt(10, 25, 15), 1, 1, 0, true) { JudgeAttempts = 3 };

        var back = Assert.IsType<MealDecisionRecordRead.Held>(MealDecisionRecordCodec.Decode(MealDecisionRecordCodec.Encode(MealDecisionRecord.Empty.With(entry)))).Record;

        Assert.Equal(3, back.For(Family.Sunday)!.JudgeAttempts);
    }

    [Fact]
    public void AnOlderRecordReadsAsNoAttemptsAndNoneIsWrittenForZero()
    {
        var entry = new MealDecisionEntry(Family.Sunday, Family.Edt(10, 25, 15), 1, 1, 0, true);

        var json = MealDecisionRecordCodec.Encode(MealDecisionRecord.Empty.With(entry));

        Assert.DoesNotContain("judgeAttempts", json, StringComparison.Ordinal);
        Assert.Equal(0, Assert.IsType<MealDecisionRecordRead.Held>(MealDecisionRecordCodec.Decode(json)).Record.For(Family.Sunday)!.JudgeAttempts);
    }
}
