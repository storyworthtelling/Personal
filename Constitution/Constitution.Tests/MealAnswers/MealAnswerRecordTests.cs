using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Household.Lease;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.ReadBack;
using Constitution.Tests.Calendar;
using Storyworthtelling.Household;
using static Constitution.Tests.MealAnswers.MealAnswerData;

namespace Constitution.Tests.MealAnswers;

/// <summary>Each install writes its own adults' answers; the planner's install reads both. Writing is idempotent and never rewrites an on-time answer with a late one.</summary>
public class MealAnswerRecordTests
{
    private const string SamInstall = "install-sam";
    private const string LeeInstall = "install-lee";

    private static MealAnswerWrite Write(HouseholdDocument document, HybridLogicalClock clock, string install, MealAnswer answer) =>
        MealAnswerRecords.Write(document, clock, install, answer, Deadline, answer.ReadAt);

    [Fact]
    public void TheAnswerIsKeptUnderTheInstallThatReadIt()
    {
        var written = Write(HouseholdDocument.Empty, new HybridLogicalClock(), SamInstall, Tick(Sam, Tuesday, Before(30)));

        var record = Assert.Single(written.Document.LiveRecords);
        Assert.Equal(SamInstall, record.Writer);
        Assert.Equal("week-plan", record.Key.Kind);
        Assert.Equal(MealAnswerOutcome.Recorded, written.Outcome);
    }

    [Fact]
    public void WritingTheSameAnswerTwiceChangesNothing()
    {
        var clock = new HybridLogicalClock();
        var first = Write(HouseholdDocument.Empty, clock, SamInstall, Deny(Sam, Tuesday, "no", Before(60)));
        var again = Write(first.Document, clock, SamInstall, Deny(Sam, Tuesday, "no", Before(10)));

        Assert.Equal(MealAnswerOutcome.AlreadyRecorded, again.Outcome);
        Assert.True(first.Document.ContentEquals(again.Document));
        // The first sight of the answer is the time that stands: a re-read of the same ink must not make it later.
        Assert.Equal(Before(60).ToUnixTimeMilliseconds(), MealAnswerRecords.Read(again.Document).Answers.Single().ReadAt.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void ASameAnswerReReadAfterTheDeadlineStaysOnTime()
    {
        var clock = new HybridLogicalClock();
        var first = Write(HouseholdDocument.Empty, clock, SamInstall, Deny(Sam, Tuesday, "no", Before(60)));
        var again = Write(first.Document, clock, SamInstall, Deny(Sam, Tuesday, "no", After(30)));

        Assert.Equal(MealAnswerOutcome.AlreadyRecorded, again.Outcome);
        Assert.Equal(MealItemOutcome.Denied, Only(MealDeadline.Decide(MealAnswerRecords.Read(again.Document).Answers, Deadline, After(60))).Outcome);
    }

    [Fact]
    public void AChangedAnswerReplacesTheOldOne()
    {
        var clock = new HybridLogicalClock();
        var first = Write(HouseholdDocument.Empty, clock, SamInstall, Silent(Sam, Tuesday, Before(90)));
        var second = Write(first.Document, clock, SamInstall, Deny(Sam, Tuesday, "no", Before(20)));

        Assert.Equal(MealAnswerOutcome.Recorded, second.Outcome);
        var answer = Assert.Single(MealAnswerRecords.Read(second.Document).Answers);
        Assert.Equal(MealVerdict.Denied, answer.Verdict);
    }

    [Fact]
    public void ALateAnswerNeverReplacesAnOnTimeOne()
    {
        var clock = new HybridLogicalClock();
        var first = Write(HouseholdDocument.Empty, clock, LeeInstall, Silent(Lee, Tuesday, Before(30)));
        var late = Write(first.Document, clock, LeeInstall, Deny(Lee, Tuesday, "no", After(30)));

        Assert.Equal(MealAnswerOutcome.TooLate, late.Outcome);
        Assert.True(first.Document.ContentEquals(late.Document));
    }

    [Fact]
    public void ALateAnswerIsKeptWhenNothingCameBefore()
    {
        var late = Write(HouseholdDocument.Empty, new HybridLogicalClock(), LeeInstall, Deny(Lee, Tuesday, "no", After(30)));
        Assert.Equal(MealAnswerOutcome.Recorded, late.Outcome);
    }

    [Fact]
    public void ALateAnswerMayReplaceAnEarlierLateOne()
    {
        var clock = new HybridLogicalClock();
        var first = Write(HouseholdDocument.Empty, clock, LeeInstall, Silent(Lee, Tuesday, After(20)));
        var second = Write(first.Document, clock, LeeInstall, Deny(Lee, Tuesday, "no", After(40)));
        Assert.Equal(MealAnswerOutcome.Recorded, second.Outcome);
    }

    [Fact]
    public void ThePartnerContributesWithoutPlanning()
    {
        var sam = new HybridLogicalClock();
        var lee = new HybridLogicalClock();
        var samFile = Write(HouseholdDocument.Empty, sam, SamInstall, Tick(Sam, Tuesday, Before(30))).Document;
        var leeFile = Write(HouseholdDocument.Empty, lee, LeeInstall, Deny(Lee, Tuesday, "Robin has swim", Before(25))).Document;

        // The partner's install is not the planner: the lease says so, and it still reads its own tablet's ink and writes the answers.
        var lease = PlanningLease.Decide(leeFile, LeeInstall, RosterRole.Partner, Deadline, InventedFamily.Zone, LeaseTerms.DefaultLeaseLength);
        Assert.Equal(LeaseVerdict.DoNotPlan, lease.Verdict);

        var planner = HouseholdMerge.Merge(samFile, leeFile);
        var answers = MealAnswerRecords.Read(planner).Answers;
        Assert.Equal(["lee", "sam"], answers.Select(answer => answer.Person.Value).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(MealItemOutcome.Denied, Only(MealDeadline.Decide(answers, Deadline, After(1))).Outcome);
    }

    [Fact]
    public void TheOrderTheFilesMeetInChangesNothing()
    {
        var samFile = Write(HouseholdDocument.Empty, new HybridLogicalClock(), SamInstall, Tick(Sam, Tuesday, Before(30))).Document;
        var leeFile = Write(HouseholdDocument.Empty, new HybridLogicalClock(), LeeInstall, Silent(Lee, Tuesday, Before(30))).Document;

        Assert.True(HouseholdMerge.Merge(samFile, leeFile).ContentEquals(HouseholdMerge.Merge(leeFile, samFile)));
    }

    [Fact]
    public void ReadingOneWeekLeavesTheOthersAlone()
    {
        var clock = new HybridLogicalClock();
        var nextWeek = new DateOnly(2026, 11, 1);
        var document = Write(HouseholdDocument.Empty, clock, SamInstall, Tick(Sam, Tuesday, Before(30))).Document;
        document = Write(document, clock, SamInstall, Tick(Sam, Tuesday, Before(20), nextWeek)).Document;

        Assert.Equal(2, MealAnswerRecords.Read(document).Answers.Count);
        Assert.Equal(nextWeek, Assert.Single(MealAnswerRecords.Read(document, nextWeek).Answers).Week);
    }

    [Fact]
    public void AnotherKindOfWeekPlanRecordIsNotAnAnswer()
    {
        var plan = HouseholdRecord.Write(new RecordKey(ConstitutionKinds.WeekPlan, "plan:2026-10-25"), 1, new Dictionary<string, string> { ["cook"] = "sam" }, new HlcTimestamp(5, 0), SamInstall);
        var read = MealAnswerRecords.Read(HouseholdDocument.Empty.With(plan));

        Assert.Empty(read.Answers);
        Assert.Empty(read.Unreadable);
    }

    [Fact]
    public void AnAnswerThatCannotBeReadIsListedNotDropped()
    {
        var key = MealAnswerMaps.Answer.KeyOf(Tick(Sam, Tuesday, Before(30)));
        var broken = HouseholdRecord.Write(key, 1, new Dictionary<string, string> { ["person"] = "sam" }, new HlcTimestamp(5, 0), SamInstall);
        var good = Write(HouseholdDocument.Empty.With(broken), new HybridLogicalClock(), SamInstall, Tick(Sam, Wednesday, Before(30))).Document;

        var read = MealAnswerRecords.Read(good);
        Assert.Equal(Wednesday, Assert.Single(read.Answers).ItemId);
        Assert.Equal(key, Assert.Single(read.Unreadable).Key);
    }

    [Fact]
    public void AnUnreadableRecordIsNeverWrittenOver()
    {
        var key = MealAnswerMaps.Answer.KeyOf(Tick(Sam, Tuesday, Before(30)));
        var fields = new Dictionary<string, string>(MealAnswerMaps.Answer.FieldsOf(Tick(Sam, Tuesday, Before(30)))) { ["verdict"] = "a-newer-word" };
        var newer = HouseholdDocument.Empty.With(HouseholdRecord.Write(key, 2, fields, new HlcTimestamp(5, 0), SamInstall));

        var written = Write(newer, new HybridLogicalClock(), SamInstall, Deny(Sam, Tuesday, "no", Before(20)));

        Assert.Equal(MealAnswerOutcome.Unreadable, written.Outcome);
        Assert.True(newer.ContentEquals(written.Document));
    }

    [Fact]
    public void ABlankInstallIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Write(HouseholdDocument.Empty, new HybridLogicalClock(), " ", Tick(Sam, Tuesday, Before(30))));
    }

    [Fact]
    public void ADeletedAnswerIsNotRead()
    {
        var clock = new HybridLogicalClock();
        var document = Write(HouseholdDocument.Empty, clock, SamInstall, Tick(Sam, Tuesday, Before(30))).Document;
        var removed = HouseholdRecords.Remove(document, MealAnswerMaps.Answer, Tick(Sam, Tuesday, Before(30)), clock.Witness(document.MaxStamp, Before(20).ToUnixTimeMilliseconds()), SamInstall);

        Assert.Empty(MealAnswerRecords.Read(removed).Answers);
    }

    [Fact]
    public void ASlowClockStillOutranksWhatItRead()
    {
        // This install's own clock runs an hour behind the other's; its answer must still win over the record it replaces.
        var leeClock = new HybridLogicalClock();
        var ahead = Write(HouseholdDocument.Empty, new HybridLogicalClock(), LeeInstall, Silent(Lee, Tuesday, Before(90))).Document;
        var slow = MealAnswerRecords.Write(ahead, leeClock, LeeInstall, Deny(Lee, Tuesday, "no", Before(20)), Deadline, Before(90).AddHours(-1));

        Assert.Equal(MealVerdict.Denied, Assert.Single(MealAnswerRecords.Read(slow.Document).Answers).Verdict);
    }

    [Fact]
    public void ExpectedLinesAreTheSheetsOwnRules()
    {
        var model = MealSheet.MealSheetSamples.FullModel();
        var expected = ExpectedDocument.ForMealSheet(model, "Sam").Pages.SelectMany(page => page.Rules).OfType<MealItemRule>().Select(rule => rule.YesBoxId).ToList();

        Assert.Equal(expected, MealAnswerRecords.ExpectedItems(model));
        Assert.Contains("dinner-tue-yes", expected);
        Assert.Contains("box-sandwich-yes", expected);
    }
}
