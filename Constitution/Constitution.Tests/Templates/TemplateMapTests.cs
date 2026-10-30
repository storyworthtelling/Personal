using Constitution.Domain.Household;
using Constitution.Domain.Household.Lease;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Templates;
using Storyworthtelling.Household;
using static Constitution.Tests.MealAnswers.MealAnswerData;
using static Constitution.Tests.Templates.TemplateData;

namespace Constitution.Tests.Templates;

/// <summary>
/// The stored shape of the library. It rides the committed <c>week-plan</c> kind under its own key prefixes, so no kind is added and the
/// plan's and the answers' records are left as they are. Every word is a literal: the file outlives the build.
/// </summary>
public class TemplateMapTests
{
    private static readonly HlcTimestamp Stamp = new(1_000, 0);
    private static readonly MealTemplate Rejection = Rejected("tpl-box-fruit-melon", MealTemplateSlot.BoxFruit, "Cold melon", "too wet");
    private static readonly MealTemplate Explicit = Approved("tpl-weekend-lunch-soup", MealTemplateSlot.WeekendLunch, "Soup and bread");
    private static readonly MealTemplate Waiting = Proposed("tpl-snack-bar-oat", MealTemplateSlot.SnackBar, "Oat bar") with { FeedbackWeek = Week };

    private static HouseholdRecord Stored(MealTemplate template) =>
        HouseholdRecord.Write(TemplateMaps.Template.KeyOf(template), RecordSchema.Version, TemplateMaps.Template.FieldsOf(template), Stamp, "install-sam");

    [Fact]
    public void NoNewRecordKindWasAdded()
    {
        Assert.Equal("week-plan", TemplateMaps.Template.Kind);
        Assert.Equal("week-plan", TemplateMaps.Print.Kind);
        Assert.Equal(["recipe", "week-plan", "rating", "taste-note", "staple", "person", "planning-lease"], ConstitutionKinds.All);
    }

    [Fact]
    public void TheKeysAreFrozen()
    {
        Assert.Equal("week-plan/template:tpl-box-fruit-melon", TemplateMaps.Template.KeyOf(Rejection).Value);
        Assert.Equal(
            "week-plan/template-print:2026-10-25:weekend-breakfast-yes",
            TemplateMaps.Print.KeyOf(new TemplatePrint(Week, "weekend-breakfast-yes", "tpl-x")).Value);
    }

    [Fact]
    public void ATemplateKeyIsNotAnAnswerKeyOrAPrintKey()
    {
        var key = TemplateMaps.Template.KeyOf(Explicit);
        var printKey = TemplateMaps.Print.KeyOf(new TemplatePrint(Week, "box-fruit-yes", Explicit.Id));

        Assert.True(TemplateKeys.IsTemplate(key));
        Assert.False(TemplateKeys.IsPrint(key));
        Assert.True(TemplateKeys.IsPrint(printKey));
        Assert.False(TemplateKeys.IsTemplate(printKey));
        Assert.False(MealAnswerKeys.IsAnswer(key));
        Assert.False(MealAnswerKeys.IsAnswer(printKey));
        Assert.False(TemplateKeys.IsTemplate(new RecordKey(ConstitutionKinds.WeekPlan, "plan:2026-10-25")));
        Assert.False(TemplateKeys.IsTemplate(new RecordKey(ConstitutionKinds.TasteNote, "template:x")));
    }

    [Fact]
    public void TheFieldNamesAreFrozen()
    {
        Assert.Equal(["approval", "feedback", "id", "reason", "slot", "status", "words"], TemplateMaps.Template.FieldsOf(Rejection).Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(["item", "template", "week"], TemplateMaps.Print.FieldsOf(new TemplatePrint(Week, "box-fruit-yes", "tpl-x")).Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void TheWordsAreFrozen()
    {
        var fields = TemplateMaps.Template.FieldsOf(Rejection);
        Assert.Equal("box-fruit", fields["slot"]);
        Assert.Equal("rejected", fields["status"]);
        Assert.Equal("none", fields["approval"]);
        Assert.Equal("too wet", fields["reason"]);
        Assert.Equal("Cold melon", fields["words"]);
        Assert.Equal("explicit", TemplateMaps.Template.FieldsOf(Explicit)["approval"]);
        Assert.Equal("silence", TemplateMaps.Template.FieldsOf(Explicit with { Approval = TemplateApproval.Silence })["approval"]);
        Assert.Equal("proposed", TemplateMaps.Template.FieldsOf(Waiting)["status"]);
        Assert.Equal("2026-10-25", TemplateMaps.Template.FieldsOf(Waiting)["feedback"]);
        Assert.Equal(
            ["weekday-breakfast", "weekday-lunch", "weekend-breakfast", "weekend-lunch", "box-sandwich", "box-fruit", "box-snack", "snack-fruit", "snack-bar"],
            Enum.GetValues<MealTemplateSlot>().Select(MealTemplateWords.Slots.Word));
    }

    [Fact]
    public void EveryStateRoundTrips()
    {
        foreach (var template in new[] { Rejection, Explicit, Waiting, Explicit with { Approval = TemplateApproval.Silence } })
        {
            Assert.Equal(template, TemplateMaps.Template.Read(new RecordReader(Stored(template))));
        }

        var print = new TemplatePrint(Week, "box-fruit-yes", "tpl-box-fruit-melon");
        var record = HouseholdRecord.Write(TemplateMaps.Print.KeyOf(print), RecordSchema.Version, TemplateMaps.Print.FieldsOf(print), Stamp, "install-sam");
        Assert.Equal(print, TemplateMaps.Print.Read(new RecordReader(record)));
    }

    [Fact]
    public void AWordThisBuildDoesNotKnowIsUnreadable()
    {
        var fields = TemplateMaps.Template.FieldsOf(Explicit).ToDictionary(pair => pair.Key, pair => pair.Value);
        fields["status"] = "maybe";
        var record = HouseholdRecord.Write(TemplateMaps.Template.KeyOf(Explicit), 1, fields, Stamp, "install-sam");

        var exception = Assert.Throws<HouseholdRecordException>(() => TemplateMaps.Template.Read(new RecordReader(record)));
        Assert.Contains("status", exception.Message);
    }

    [Fact]
    public void ARecordFilledOutOfItsKeyIsUnreadable()
    {
        var record = HouseholdRecord.Write(TemplateMaps.Template.KeyOf(Rejection), 1, TemplateMaps.Template.FieldsOf(Explicit), Stamp, "install-sam");

        Assert.Throws<HouseholdRecordException>(() => TemplateMaps.Template.Read(new RecordReader(record)));
    }

    [Fact]
    public void ARecordTheRulesRefuseIsUnreadableNotACrash()
    {
        var fields = TemplateMaps.Template.FieldsOf(Explicit).ToDictionary(pair => pair.Key, pair => pair.Value);
        fields["approval"] = "none";
        var record = HouseholdRecord.Write(TemplateMaps.Template.KeyOf(Explicit), 1, fields, Stamp, "install-sam");

        Assert.Throws<HouseholdRecordException>(() => TemplateMaps.Template.Read(new RecordReader(record)));
    }

    private static TemplateLibrary Library() =>
        new([Rejection, Explicit, Waiting], [new TemplatePrint(Week, "snack-bar-yes", Waiting.Id), new TemplatePrint(Week, "weekend-lunch-yes", Explicit.Id)]);

    [Fact]
    public void ALibrarySavesAndReadsBack()
    {
        var saved = TemplateRecords.Save(HouseholdDocument.Empty, new HybridLogicalClock(), "install-sam", Library(), Deadline);

        var read = TemplateRecords.Read(saved);

        Assert.Empty(read.Unreadable);
        Assert.Equal(Library().Templates, read.Library.Templates);
        Assert.Equal(Library().Prints, read.Library.Prints);
    }

    [Fact]
    public void SavingTwiceChangesNothing()
    {
        var clock = new HybridLogicalClock();
        var once = TemplateRecords.Save(HouseholdDocument.Empty, clock, "install-sam", Library(), Deadline);
        var twice = TemplateRecords.Save(once, clock, "install-sam", Library(), After(60));

        Assert.True(once.ContentEquals(twice));
    }

    [Fact]
    public void AChangedTemplateEditsItsOneRecord()
    {
        var clock = new HybridLogicalClock();
        var once = TemplateRecords.Save(HouseholdDocument.Empty, clock, "install-sam", Library(), Deadline);
        var changed = TemplateRecords.Save(once, clock, "install-lee", Library().With(Waiting with { Status = TemplateStatus.Approved, Approval = TemplateApproval.Silence }), After(60));

        Assert.Equal(once.LiveRecords.Count, changed.LiveRecords.Count);
        Assert.Equal(TemplateStatus.Approved, TemplateRecords.Read(changed).Library.Find(Waiting.Id)!.Status);
    }

    [Fact]
    public void OtherWeekPlanRecordsAreNeitherReadNorTouched()
    {
        var clock = new HybridLogicalClock();
        var plan = HouseholdRecord.Write(new RecordKey(ConstitutionKinds.WeekPlan, "plan:2026-10-25"), 1, new Dictionary<string, string> { ["anything"] = "stays" }, Stamp, "install-sam");
        var answered = MealAnswerRecords.Write(HouseholdDocument.Empty.With(plan), clock, "install-sam", Tick(Sam, "dinner-tue-yes", Before(30)), Deadline, Before(30)).Document;

        var saved = TemplateRecords.Save(answered, clock, "install-sam", Library(), After(1));

        Assert.True(saved.Find(plan.Key)!.ContentEquals(plan));
        Assert.Single(MealAnswerRecords.Read(saved).Answers);
        Assert.Empty(MealAnswerRecords.Read(saved).Unreadable);
        Assert.Empty(TemplateRecords.Read(answered).Library.Templates);
        Assert.Empty(TemplateRecords.Read(saved).Unreadable);
        Assert.Equal(3, TemplateRecords.Read(saved).Library.Templates.Count);
    }

    [Fact]
    public void NoTasteNoteRecordsAreWritten()
    {
        var saved = TemplateRecords.Save(HouseholdDocument.Empty, new HybridLogicalClock(), "install-sam", Library(), Deadline);

        Assert.All(saved.Records, record => Assert.Equal(ConstitutionKinds.WeekPlan, record.Key.Kind));
    }

    [Fact]
    public void AnUnreadableRecordIsListedAndNeverWrittenOver()
    {
        var fields = TemplateMaps.Template.FieldsOf(Explicit).ToDictionary(pair => pair.Key, pair => pair.Value);
        fields["status"] = "a-newer-word";
        var odd = HouseholdRecord.Write(TemplateMaps.Template.KeyOf(Explicit), 1, fields, Stamp, "install-newer");
        var document = HouseholdDocument.Empty.With(odd);

        var saved = TemplateRecords.Save(document, new HybridLogicalClock(), "install-sam", Library(), Deadline);

        Assert.Equal("a-newer-word", saved.Find(odd.Key)!.Fields["status"]);
        Assert.Equal(odd.Key, Assert.Single(TemplateRecords.Read(saved).Unreadable).Key);
    }

    [Fact]
    public void AFieldANewerWriterAddedSurvivesAnEdit()
    {
        var clock = new HybridLogicalClock();
        var fields = TemplateMaps.Template.FieldsOf(Waiting).ToDictionary(pair => pair.Key, pair => pair.Value);
        fields["future"] = "kept";
        var stored = HouseholdRecord.Write(TemplateMaps.Template.KeyOf(Waiting), 2, fields, Stamp, "install-newer");

        var saved = TemplateRecords.Save(HouseholdDocument.Empty.With(stored), clock, "install-sam", Library().With(Waiting with { Status = TemplateStatus.Approved, Approval = TemplateApproval.Explicit }), After(1));

        Assert.Equal("kept", saved.Find(stored.Key)!.Fields["future"]);
    }
}
