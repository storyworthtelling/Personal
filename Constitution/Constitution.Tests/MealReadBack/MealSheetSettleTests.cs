using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Infrastructure.Household;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.HouseholdFile;
using Constitution.Tests.MealSheet;
using Storyworthtelling.Household;
using static Constitution.Tests.MealReadBack.MealRig;

namespace Constitution.Tests.MealReadBack;

/// <summary>What a settled meal sheet leaves in the household file: this tablet owner's answers, and nothing a read could not settle.</summary>
public class MealSheetSettleTests
{
    private static readonly PersonId SamId = new("sam");
    private static readonly PersonId LeeId = new("lee");

    private static Func<DayOfWeek, InkResolution?> Marks(DayOfWeek ticked, DayOfWeek denied, string reason) => day =>
        day == ticked ? new InkResolution.Ticked(Yes(day))
        : day == denied ? new InkResolution.Denied(Yes(day), reason)
        : null;

    [Fact]
    public void TicksAndDenialsAreTheOwnersAnswers()
    {
        using var rig = new MealRig();
        var document = Sheet();
        var result = SheetResult(document, Before, Marks(DayOfWeek.Monday, DayOfWeek.Tuesday, "too heavy for a school night"));

        rig.Hook.Settled(document, result);

        var answers = rig.Answers();
        Assert.Equal(7, answers.Count);
        Assert.All(answers, answer => Assert.Equal(SamId, answer.Person));
        Assert.Equal(MealVerdict.Approved, answers.Single(a => a.ItemId == Yes(DayOfWeek.Monday)).Verdict);
        var denied = answers.Single(a => a.ItemId == Yes(DayOfWeek.Tuesday));
        Assert.Equal(MealVerdict.Denied, denied.Verdict);
        Assert.Equal("too heavy for a school night", denied.Reason);
        Assert.Equal(5, answers.Count(a => a.Verdict == MealVerdict.Unchanged));
        Assert.All(answers, answer => Assert.Equal(Before.ToUnixTimeMilliseconds(), answer.ReadAt.ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void ThePartnerInstallWritesItsOwnAdultsAnswers()
    {
        using var rig = new MealRig(HouseholdFileRole.SharedWith);
        var document = Sheet("Lee");

        rig.Hook.Settled(document, SheetResult(document, Before, Marks(DayOfWeek.Monday, DayOfWeek.Tuesday, "no")));

        Assert.All(rig.Answers(), answer => Assert.Equal(LeeId, answer.Person));
        Assert.Equal(7, rig.Answers().Count);
    }

    [Fact]
    public void TheNameIsFoundWithoutCase()
    {
        using var rig = new MealRig();
        var document = Sheet("SAM");

        rig.Hook.Settled(document, SheetResult(document, Before));

        Assert.All(rig.Answers(), answer => Assert.Equal(SamId, answer.Person));
    }

    [Fact]
    public void ANameNotOnTheRosterWritesNothingAndSaysSo()
    {
        using var rig = new MealRig();
        var before = rig.Document();
        var document = Sheet("Nobody");

        Assert.ThrowsAny<InvalidOperationException>(() => rig.Hook.Settled(document, SheetResult(document, Before)));

        Assert.True(before.ContentEquals(rig.Document()));
    }

    [Fact]
    public void AChildIsNeverATabletOwner()
    {
        using var rig = new MealRig();
        var document = Sheet("Robin");

        Assert.ThrowsAny<InvalidOperationException>(() => rig.Hook.Settled(document, SheetResult(document, Before)));

        Assert.Empty(rig.Answers());
    }

    [Fact]
    public void TwoAdultsOfOneNameAreNotGuessedBetween()
    {
        using var rig = new MealRig();
        var twin = new RosterPerson(new PersonId("sam-two"), "Sam", MemberRole.Adult, null, false, null);
        rig.Edit(document => new RosterEditor(RosterRole.Owner).Save(document, twin, RosterData.At(3000), RosterData.OwnerWriter).Document);
        var document = Sheet();

        Assert.ThrowsAny<InvalidOperationException>(() => rig.Hook.Settled(document, SheetResult(document, Before)));

        Assert.Empty(rig.Answers());
    }

    [Fact]
    public void LinesTheReadCouldNotSettleWriteNothing()
    {
        using var rig = new MealRig(withHousehold: false);
        var document = Sheet();
        var result = SheetResult(
            document,
            After,
            day => (int)day % 2 == 0
                ? new InkResolution.NeedsAsking(Yes(day), "dinner", "Keep it", "Leave it out", "cause-" + day)
                : new InkResolution.NotRead(Yes(day), "The page was not read."));

        rig.Hook.Settled(document, result);

        Assert.Empty(rig.Document().Records);
    }

    [Fact]
    public void AnUnsettledLineIsLeftOutBesideSettledOnes()
    {
        using var rig = new MealRig();
        var document = Sheet();
        var result = SheetResult(
            document,
            After,
            day => day == DayOfWeek.Monday
                ? new InkResolution.NeedsAsking(Yes(day), "Monday dinner", "Keep it", "Leave it out", "cause")
                : day == DayOfWeek.Tuesday ? new InkResolution.Ticked(Yes(day)) : null);

        rig.Hook.Settled(document, result);

        var answers = rig.Answers();
        Assert.DoesNotContain(answers, answer => answer.ItemId == Yes(DayOfWeek.Monday));
        Assert.Equal(6, answers.Count);
        Assert.Equal(MealVerdict.Approved, answers.Single(a => a.ItemId == Yes(DayOfWeek.Tuesday)).Verdict);
    }

    [Fact]
    public void ALineThatIsNotADinnerIsNotAnAnswer()
    {
        using var rig = new MealRig();
        var document = Sheet();
        var result = SheetResult(
            document,
            After,
            null,
            new InkResolution.Ticked("staple-rice"),
            new InkResolution.Needed("staple-oats", "two bags"),
            new InkResolution.NoNeed("staple-salt"),
            new InkResolution.Denied("breakfast-pancakes", "no"));

        rig.Hook.Settled(document, result);

        Assert.Equal(7, rig.Answers().Count);
        Assert.All(rig.Answers(), answer => Assert.StartsWith("dinner", answer.ItemId, StringComparison.Ordinal));
    }

    [Fact]
    public void TheSameSheetSettledTwiceChangesNothing()
    {
        using var rig = new MealRig();
        var document = Sheet();
        var result = SheetResult(document, Before, Marks(DayOfWeek.Monday, DayOfWeek.Tuesday, "no"));
        rig.Hook.Settled(document, result);
        var once = rig.Document();

        rig.Hook.Settled(document, result with { ReadAt = After });

        Assert.True(once.ContentEquals(rig.Document()));
        Assert.All(rig.Answers(), answer => Assert.Equal(Before.ToUnixTimeMilliseconds(), answer.ReadAt.ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void ALateReadingNeverReplacesAnOnTimeOne()
    {
        using var rig = new MealRig();
        var document = Sheet();
        rig.Hook.Settled(document, SheetResult(document, Before));

        rig.Hook.Settled(document, SheetResult(document, After, Marks(DayOfWeek.Monday, DayOfWeek.Tuesday, "too late to matter")));

        Assert.All(rig.Answers(), answer => Assert.Equal(MealVerdict.Unchanged, answer.Verdict));
    }

    [Fact]
    public void AnUnreadableFileIsNeverWrittenOver()
    {
        using var rig = new MealRig();
        rig.MakeUnreadable();
        var before = rig.Document();
        var document = Sheet();

        Assert.Throws<HouseholdUnreadableException>(() => rig.Hook.Settled(document, SheetResult(document, Before, Marks(DayOfWeek.Monday, DayOfWeek.Tuesday, "no"))));

        Assert.True(before.ContentEquals(rig.Document()));
    }

    [Fact]
    public void AnAnswerThisBuildCannotReadIsLeftAndReported()
    {
        using var rig = new MealRig();
        var document = Sheet();
        var wrong = new MealAnswer(SamId, MealSheetSamples.Sunday, Yes(DayOfWeek.Monday), MealVerdict.Approved, null, Before);
        var garbled = MealAnswerMaps.Answer.FieldsOf(wrong).ToDictionary(pair => pair.Key, pair => pair.Value);
        garbled["verdict"] = "a-word-from-a-newer-build";
        var record = HouseholdRecord.Write(MealAnswerKeys.KeyOf(wrong), RecordSchema.Version, garbled, RosterData.At(5000), RosterData.PartnerWriter);
        rig.Edit(held => held.With(record));

        Assert.ThrowsAny<InvalidOperationException>(() => rig.Hook.Settled(document, SheetResult(document, Before)));

        Assert.Equal(garbled, rig.Document().Live(MealAnswerKeys.KeyOf(wrong))!.Fields);
        Assert.Equal(6, rig.Answers().Count);
    }

    [Fact]
    public void AWrittenReasonIsKeptOnlyOnItsAnswer()
    {
        using var rig = new MealRig();
        var document = Sheet();

        rig.Hook.Settled(document, SheetResult(document, Before, Marks(DayOfWeek.Monday, DayOfWeek.Tuesday, "Robin will not eat lentils")));

        Assert.Equal(["person", "recipe", "week-plan"], rig.Kinds());
    }

    [Fact]
    public void OtherDocumentsAreLeftAlone()
    {
        using var rig = new MealRig();
        var before = rig.Document();
        var workout = Constitution.Tests.ReadBack.ReadBackFixture.Workout();
        var result = new Constitution.Domain.ReadBack.DocumentResult(workout.Name, workout.Kind, workout.Day, workout.Person, Before, []);

        rig.Hook.Settled(workout, result);
        rig.Hook.Observed(workout, result);

        Assert.True(before.ContentEquals(rig.Document()));
    }

    [Fact]
    public void ObservingASheetKeptForItsDeadlineRecordsWhatIsSettled()
    {
        using var rig = new MealRig();
        var document = Sheet();

        rig.Hook.Observed(document, SheetResult(document, Before, Marks(DayOfWeek.Monday, DayOfWeek.Tuesday, "no")));

        var answers = rig.Answers();
        Assert.Equal(MealVerdict.Approved, answers.Single(a => a.ItemId == Yes(DayOfWeek.Monday)).Verdict);
        Assert.Equal(MealVerdict.Denied, answers.Single(a => a.ItemId == Yes(DayOfWeek.Tuesday)).Verdict);
    }
}
