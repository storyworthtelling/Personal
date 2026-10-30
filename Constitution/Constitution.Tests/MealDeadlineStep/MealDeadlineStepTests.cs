using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Constitution.Tests.MealTaste;
using Constitution.Tests.TemplatesInTheRun;
using Family = Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealAnswers.MealAnswerData;
using static Constitution.Tests.MealDeadlineStep.DeadlineRig;

namespace Constitution.Tests.MealDeadlineStep;

/// <summary>What happens on the planner's phone once the sheet's deadline has passed. Sam and Lee, an invented week, invented words.</summary>
public class MealDeadlineStepTests
{
    // ---- who, when, once ----

    [Fact]
    public async Task ThePartnerNeverDecides()
    {
        var rig = await PlannedAsync();
        rig.MayPlan = false;

        var outcome = await rig.RunAsync();

        var stopped = Assert.IsType<MealDeadlineOutcome.NotThePlanner>(outcome);
        Assert.False(string.IsNullOrWhiteSpace(stopped.Sentence));
        Assert.Equal(1, rig.LeaseAsks);
        Assert.Equal(0, rig.Decisions.Loads);
        rig.AssertNothingTouched();
    }

    [Fact]
    public async Task BeforeTheDeadlineNothingIsDecided()
    {
        var rig = await PlannedAsync();
        rig.Now = Family.Edt(10, 24, 12);
        rig.Answers.All.Add(Deny(Sam, rig.CookedDinner().Line, "no", Before(60)));

        var outcome = await rig.RunAsync();

        Assert.Equal(MealDeadlineNothingBecause.BeforeTheDeadline, Assert.IsType<MealDeadlineOutcome.NothingToDecide>(outcome).Because);
        rig.AssertNothingTouched();
    }

    [Fact]
    public async Task NoKeptWeekMeansNothingToDecide()
    {
        var rig = await PlannedAsync();
        rig.Weeks.Json = null;

        var outcome = await rig.RunAsync();

        Assert.Equal(MealDeadlineNothingBecause.NoWeekKept, Assert.IsType<MealDeadlineOutcome.NothingToDecide>(outcome).Because);
        rig.AssertNothingTouched();
    }

    [Fact]
    public async Task ADecidedWeekIsNeverDecidedTwice()
    {
        var rig = await PlannedAsync();
        rig.Answers.All.Add(Tick(Sam, rig.CookedDinner().Line, Before(60)));
        await rig.DecidedAsync();
        var reads = rig.Answers.Reads;

        var outcome = await rig.RunAsync();

        Assert.Equal(MealDeadlineNothingBecause.AlreadyDecided, Assert.IsType<MealDeadlineOutcome.NothingToDecide>(outcome).Because);
        Assert.Equal(reads, rig.Answers.Reads);
        Assert.Equal(1, rig.Decisions.Saves);
        Assert.Equal(Week, Assert.Single(rig.Decisions.Held().Entries).Week);
    }

    // ---- the verdict, counted ----

    [Fact]
    public async Task UnreadLinesApproveAndAreCounted()
    {
        var rig = await PlannedAsync();

        var report = await rig.DecidedAsync();

        // Seven dinners and the one printed template line; nobody was read for any of them (D103).
        Assert.Equal(8, report.Approved);
        Assert.Equal(8, report.ApprovedUnread);
        Assert.Equal(0, report.Denied);
        Assert.False(report.NeedsRevisedSheet);
        Assert.Empty(rig.Notes.Kept);
    }

    [Fact]
    public async Task ALineSomeoneReadIsNotCountedUnread()
    {
        var rig = await PlannedAsync();
        rig.Answers.All.Add(Silent(Sam, rig.CookedDinner().Line, Before(60)));

        var report = await rig.DecidedAsync();

        Assert.Equal(7, report.ApprovedUnread);
        Assert.Equal(8, report.Approved);
    }

    [Fact]
    public async Task ALateDenialIsSetAsideNotObeyed()
    {
        var rig = await PlannedAsync();
        rig.Answers.All.Add(Deny(Sam, rig.CookedDinner().Line, "not that", After(90)));

        var report = await rig.DecidedAsync();

        Assert.Equal(0, report.Denied);
        Assert.Equal(1, report.LateSetAside);
        Assert.False(report.NeedsRevisedSheet);
    }

    // ---- a denied dinner ----

    [Fact]
    public async Task DenialEvidenceCarriesTheDinnersCard()
    {
        var rig = await PlannedAsync();
        rig.Judge = new FakeTasteJudge(_ => new TasteJudgement.OneOff());
        var (line, card, _) = rig.CookedDinner(1);
        rig.Answers.All.Add(Deny(Sam, line, "too heavy for a school night", Before(60)));
        rig.Answers.All.Add(Deny(Lee, line, "  ", Before(50)));
        rig.Answers.All.Add(Tick(Lee, rig.CookedDinner(0).Line, Before(50)));

        var report = await rig.DecidedAsync();

        var asked = Assert.Single(rig.Judge.Asked);
        Assert.Equal("sam", asked.Person.Value);
        Assert.Equal(TasteEvidenceKind.Denial, asked.Kind);
        Assert.Equal(card, asked.Recipe);
        Assert.Equal("too heavy for a school night", asked.Words);
        Assert.Equal(1, report.OneOff);
        Assert.Equal(1, report.Denied);
    }

    [Fact]
    public async Task ALastingLessonIsSavedAsANote()
    {
        var rig = await PlannedAsync();
        var (line, card, _) = rig.CookedDinner();
        rig.Judge = new FakeTasteJudge(_ => new TasteJudgement.Lasting([new TasteLesson(TasteSubjectKind.Dish, card.Value, TasteStance.Dislikes)]));
        rig.Answers.All.Add(Deny(Sam, line, "never fish on a weeknight", Before(60)));

        var report = await rig.DecidedAsync();

        var note = Assert.Single(rig.Notes.Kept);
        Assert.Equal(TasteStance.Dislikes, note.Stance);
        Assert.Equal(TasteOrigin.Learned, note.Origin);
        Assert.Equal(card.Value, note.Subject);
        Assert.Equal(1, report.Lasting);
        Assert.Equal(0, report.Unjudged);
    }

    [Fact]
    public async Task ADenialWithoutWordsIsNotEvidence()
    {
        var rig = await PlannedAsync();
        rig.Judge = new FakeTasteJudge(_ => new TasteJudgement.OneOff());
        rig.Answers.All.Add(Deny(Sam, rig.CookedDinner().Line, null, Before(60)));

        await rig.DecidedAsync();

        Assert.Empty(rig.Judge.Asked);
    }

    [Fact]
    public async Task ADeniedEveningAheadNeedsARevisedSheetAndNamesItsDish()
    {
        var rig = await PlannedAsync();
        var (line, card, _) = rig.CookedDinner(1);
        rig.Answers.All.Add(Deny(Lee, line, "no", Before(60)));

        var report = await rig.DecidedAsync();

        Assert.True(report.NeedsRevisedSheet);
        Assert.Equal(card, Assert.Single(report.ReplanWithout));
        var entry = Assert.Single(rig.Decisions.Held().Entries);
        Assert.True(entry.NeedsRevisedSheet);
        Assert.Equal(card, Assert.Single(entry.ReplanWithout));
    }

    [Fact]
    public async Task ADeniedEveningThatIsOverNeedsNothing()
    {
        var rig = await PlannedAsync();
        var (line, _, day) = rig.CookedDinner();
        rig.Now = Family.Edt(10, 28, 12);
        rig.Answers.All.Add(Deny(Lee, line, "no", Before(60)));

        var report = await rig.DecidedAsync();

        Assert.True(day < new DateOnly(2026, 10, 28));
        Assert.Equal(1, report.Denied);
        Assert.False(report.NeedsRevisedSheet);
        Assert.Empty(report.ReplanWithout);
    }

    [Fact]
    public async Task NoJudgeMeansUnjudgedAndAskedAgainNextTime()
    {
        var rig = await PlannedAsync();
        var (line, card, _) = rig.CookedDinner();
        rig.Answers.All.Add(Deny(Sam, line, "not again", Before(60)));

        var first = await rig.DecidedAsync();
        Assert.Equal(1, first.Unjudged);
        Assert.True(rig.Decisions.Held().For(Week)!.EvidencePending);

        rig.Judge = new FakeTasteJudge(_ => new TasteJudgement.Lasting([new TasteLesson(TasteSubjectKind.Dish, card.Value, TasteStance.Dislikes)]));
        var librarySaves = rig.Library.Saves;
        var second = await rig.DecidedAsync();

        Assert.True(second.WasRetry);
        Assert.Equal(1, second.Lasting);
        Assert.Single(rig.Judge.Asked);
        Assert.False(rig.Decisions.Held().For(Week)!.EvidencePending);
        Assert.Equal(librarySaves, rig.Library.Saves);
        Assert.Single(rig.Notes.Kept);

        var third = await rig.RunAsync();
        Assert.Equal(MealDeadlineNothingBecause.AlreadyDecided, Assert.IsType<MealDeadlineOutcome.NothingToDecide>(third).Because);
    }

    [Fact]
    public async Task AskingAgainNeverTeachesALikeTwice()
    {
        var rig = await PlannedAsync();
        rig.Answers.All.Add(Tick(Sam, TickedLine, Before(90)));
        rig.Answers.All.Add(Tick(Lee, TickedLine, Before(80)));
        rig.Answers.All.Add(Deny(Sam, rig.CookedDinner().Line, "not again", Before(60)));
        await rig.DecidedAsync();
        var saves = rig.Notes.Saves;
        var likes = rig.Notes.Kept.Count;
        rig.Judge = new FakeTasteJudge(_ => new TasteJudgement.OneOff());

        var again = await rig.DecidedAsync();

        Assert.True(again.WasRetry);
        Assert.Equal(0, again.LikesTaught);
        Assert.Equal(0, again.TemplatesFed);
        Assert.Equal(saves, rig.Notes.Saves);
        Assert.Equal(likes, rig.Notes.Kept.Count);
    }

    [Fact]
    public async Task AJudgeThatStillDoesNotAnswerKeepsTheWordsPending()
    {
        var rig = await PlannedAsync();
        rig.Answers.All.Add(Deny(Sam, rig.CookedDinner().Line, "not again", Before(60)));
        await rig.DecidedAsync();

        var again = await rig.DecidedAsync();

        Assert.True(again.WasRetry);
        Assert.Equal(1, again.Unjudged);
        Assert.True(rig.Decisions.Held().For(Week)!.EvidencePending);
        Assert.Equal(2, rig.Decisions.Saves);
    }

    // ---- the rotating templates ----

    [Fact]
    public async Task ATickedTemplateIsApprovedAndEachTickerLikesIt()
    {
        var rig = await PlannedAsync();
        rig.Answers.All.Add(Tick(Sam, TickedLine, Before(90)));
        rig.Answers.All.Add(Tick(Lee, TickedLine, Before(80)));

        var report = await rig.DecidedAsync();

        var template = rig.Library.Library.Find(TickedTemplate)!;
        Assert.Equal(TemplateStatus.Approved, template.Status);
        Assert.Equal(TemplateApproval.Explicit, template.Approval);
        Assert.Equal(1, rig.Library.Saves);
        Assert.Equal(1, report.TemplatesFed);
        Assert.Equal(2, report.LikesTaught);
        Assert.Equal(["lee", "sam"], rig.Notes.Kept.Select(note => note.Person.Value).Order());
        Assert.All(rig.Notes.Kept, note =>
        {
            Assert.Equal(TasteStance.Likes, note.Stance);
            Assert.Equal(TickedTemplate, note.Subject);
        });
    }

    [Fact]
    public async Task SilenceApprovesATemplateAndTeachesNothing()
    {
        var rig = await PlannedAsync();

        await rig.DecidedAsync();

        var template = rig.Library.Library.Find(TickedTemplate)!;
        Assert.Equal(TemplateStatus.Approved, template.Status);
        Assert.Equal(TemplateApproval.Silence, template.Approval);
        Assert.Empty(rig.Notes.Kept);
        Assert.Equal(0, rig.Notes.Saves);
    }

    [Fact]
    public async Task ADeniedTemplateIsRejectedAndItsWordsGoToTheJudge()
    {
        var rig = await PlannedAsync();
        rig.Judge = new FakeTasteJudge(_ => new TasteJudgement.OneOff());
        rig.Answers.All.Add(Deny(Lee, TickedLine, "we are out of oats", Before(60)));

        var report = await rig.DecidedAsync();

        var template = rig.Library.Library.Find(TickedTemplate)!;
        Assert.Equal(TemplateStatus.Rejected, template.Status);
        var asked = Assert.Single(rig.Judge.Asked);
        Assert.Equal(TickedTemplate, asked.Recipe!.Value.Value);
        Assert.Equal("we are out of oats", asked.Words);
        Assert.Equal(1, report.TemplatesFed);
        Assert.True(report.NeedsRevisedSheet);
        Assert.Empty(report.ReplanWithout);
    }

    [Fact]
    public async Task NoLibraryYetStillDecidesTheDinners()
    {
        var rig = await PlannedAsync();
        rig.Library.Answer = new TemplateLibraryRead.NoneYet("No household file yet.");

        var report = await rig.DecidedAsync();

        Assert.Equal(7, report.Approved);
        Assert.Equal(0, report.TemplatesFed);
        Assert.Equal(0, rig.Library.Saves);
    }

    // ---- fail closed ----

    [Fact]
    public async Task AnUnreadableDecisionRecordStopsTheStep()
    {
        var rig = await PlannedAsync();
        rig.Decisions.Json = "{ not json";

        var outcome = await rig.RunAsync();

        Assert.Equal(MealDeadlineStoppedBecause.DecisionsUnreadable, Assert.IsType<MealDeadlineOutcome.Stopped>(outcome).Because);
        Assert.Equal(0, rig.Answers.Reads);
        Assert.Equal(0, rig.Decisions.Saves);
    }

    [Fact]
    public async Task UnreadableSavedWeeksStopTheStep()
    {
        var rig = await PlannedAsync();
        rig.Weeks.Json = "{ not json";

        var outcome = await rig.RunAsync();

        Assert.Equal(MealDeadlineStoppedBecause.WeeksUnreadable, Assert.IsType<MealDeadlineOutcome.Stopped>(outcome).Because);
        rig.AssertNothingTouched();
    }

    [Fact]
    public async Task AFileThatCannotBeReadStopsTheStep()
    {
        var rig = await PlannedAsync();
        rig.Answers.Cannot = "The household file cannot be read.";

        var outcome = await rig.RunAsync();

        Assert.Equal(MealDeadlineStoppedBecause.AnswersUnreadable, Assert.IsType<MealDeadlineOutcome.Stopped>(outcome).Because);
        Assert.Equal(0, rig.Decisions.Saves);
        Assert.Equal(0, rig.Library.Saves);
    }

    [Fact]
    public async Task AnAnswerRecordThatCannotBeReadStopsTheStep()
    {
        var rig = await PlannedAsync();
        rig.Answers.UnreadableRecords = 1;

        var outcome = await rig.RunAsync();

        Assert.Equal(MealDeadlineStoppedBecause.AnswersUnreadable, Assert.IsType<MealDeadlineOutcome.Stopped>(outcome).Because);
        Assert.Equal(0, rig.Decisions.Saves);
    }

    [Fact]
    public async Task AnUnreadableLibraryStopsTheStep()
    {
        var rig = await PlannedAsync();
        rig.Library.Answer = new TemplateLibraryRead.Unreadable("The household file cannot be read.");

        var outcome = await rig.RunAsync();

        Assert.Equal(MealDeadlineStoppedBecause.LibraryUnreadable, Assert.IsType<MealDeadlineOutcome.Stopped>(outcome).Because);
        Assert.Equal(0, rig.Decisions.Saves);
        Assert.Equal(0, rig.Notes.Saves);
    }

    [Fact]
    public async Task NotesThatCannotBeSavedLeaveTheWeekUndecidedAndLoseNothing()
    {
        var rig = await PlannedAsync();
        rig.Answers.All.Add(Tick(Sam, TickedLine, Before(90)));
        rig.Notes.RefuseWith = "The household file cannot be written.";

        var outcome = await rig.RunAsync();

        Assert.Equal(MealDeadlineStoppedBecause.NotesNotSaved, Assert.IsType<MealDeadlineOutcome.Stopped>(outcome).Because);
        Assert.Equal(0, rig.Library.Saves);
        Assert.Equal(0, rig.Decisions.Saves);

        rig.Notes.RefuseWith = null;
        var report = await rig.DecidedAsync();
        Assert.Equal(1, report.LikesTaught);
        Assert.Single(rig.Notes.Kept);
    }

    [Fact]
    public async Task ALibraryThatCannotBeSavedLeavesTheWeekUndecidedAndLosesNoLike()
    {
        var rig = await PlannedAsync();
        rig.Answers.All.Add(Tick(Sam, TickedLine, Before(90)));
        rig.Library.RefuseWith = "The household file cannot be written.";

        var outcome = await rig.RunAsync();

        Assert.Equal(MealDeadlineStoppedBecause.LibraryNotSaved, Assert.IsType<MealDeadlineOutcome.Stopped>(outcome).Because);
        Assert.Equal(0, rig.Decisions.Saves);

        rig.Library.RefuseWith = null;
        var report = await rig.DecidedAsync();
        Assert.Equal(1, report.TemplatesFed);
        Assert.Single(rig.Notes.Kept);
        Assert.Equal(TemplateApproval.Explicit, rig.Library.Library.Find(TickedTemplate)!.Approval);
    }

    [Fact]
    public async Task TheStepNeverTouchesTheKeptWeeks()
    {
        var rig = await PlannedAsync();
        var saves = rig.Weeks.Saves;
        rig.Answers.All.Add(Deny(Sam, rig.CookedDinner().Line, "no", Before(60)));

        await rig.DecidedAsync();

        Assert.Equal(saves, rig.Weeks.Saves);
    }

    // ---- the trail ----

    [Fact]
    public async Task TheTrailHoldsCountsAndNothingElse()
    {
        var rig = await PlannedAsync();
        rig.Answers.All.Add(Deny(Sam, rig.CookedDinner().Line, "private words about a dish", Before(60)));

        await rig.DecidedAsync();

        var line = Assert.Single(rig.Log.Lines);
        Assert.StartsWith("meal deadline decided", line, StringComparison.Ordinal);
        Assert.DoesNotContain("private", line, StringComparison.Ordinal);
        Assert.Contains("1 lines denied", line, StringComparison.Ordinal);
    }
}
