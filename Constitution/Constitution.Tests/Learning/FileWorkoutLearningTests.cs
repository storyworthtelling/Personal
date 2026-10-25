using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.ReadBack;

namespace Constitution.Tests.Learning;

public class FileWorkoutLearningTests
{
    private const string Person = ReadBackFixture.Person;

    [Fact]
    public void NothingStoredLoadsNothing()
    {
        using var folder = new LearningFolder();

        var loaded = new FileWorkoutLearning(folder.Ledger(), folder.Path).Load();

        Assert.Null(loaded.UnreadableBecause);
        Assert.Equal(ProgressionLedger.Empty.ToJson(), loaded.Progression.ToJson());
        Assert.Empty(loaded.Baselines);
    }

    [Fact]
    public void ASettledDayTeachesTheLadders()
    {
        using var folder = new LearningFolder();
        var learning = new FileWorkoutLearning(folder.Ledger(), folder.Path);
        var (document, result) = Resolved.Strength((2, "done"), (3, "done"));

        learning.Settled(document, result);

        var again = new FileWorkoutLearning(folder.Ledger(), folder.Path).Load();
        Assert.NotEqual(ProgressionLedger.Empty.ToJson(), again.Progression.ToJson());
    }

    [Fact]
    public void TheSameDayTwiceLearnsOnce()
    {
        using var folder = new LearningFolder();
        var learning = new FileWorkoutLearning(folder.Ledger(), folder.Path);
        var (document, result) = Resolved.Strength((2, "done"), (3, "done"));

        learning.Settled(document, result);
        var once = File.ReadAllText(folder.File("progression.json"));
        learning.Settled(document, result);

        Assert.Equal(once, File.ReadAllText(folder.File("progression.json")));
    }

    [Fact]
    public void TheSharedSkipLedgerIsTheOneRead()
    {
        using var folder = new LearningFolder();
        var shared = folder.Ledger();
        var (document, result) = Resolved.Strength((2, "done"));
        shared.Save(SkipLedger.Empty.Settle(document.Marks!, result.All));

        var loaded = new FileWorkoutLearning(shared, folder.Path).Load();

        Assert.Equal(ReadBackFixture.Day, loaded.HistoryOf(Person).LastDoneDay);
    }

    [Fact]
    public void AnUnreadableSkipLedgerIsToldNotEmpty()
    {
        using var folder = new LearningFolder();
        File.WriteAllText(folder.File("skips.json"), "{ not a ledger");

        var loaded = new FileWorkoutLearning(folder.Ledger(), folder.Path).Load();

        Assert.NotNull(loaded.UnreadableBecause);
        Assert.Equal("{ not a ledger", File.ReadAllText(folder.File("skips.json")));
    }

    [Fact]
    public void APartlyReadableSkipLedgerStillTeaches()
    {
        // The skip ledger keeps what it cannot interpret and learning only reads it, so one odd entry is no reason to stop learning.
        using var folder = new LearningFolder();
        File.WriteAllText(
            folder.File("skips.json"),
            "{\"version\":1,\"records\":[{\"person\":\"Juniper\",\"day\":\"2026-10-31\",\"kind\":\"yoga\",\"outcome\":\"done\"}]}");
        var learning = new FileWorkoutLearning(folder.Ledger(), folder.Path);
        var (document, result) = Resolved.Strength((2, "done"), (3, "done"));

        Assert.Null(learning.Load().UnreadableBecause);
        learning.Settled(document, result);

        Assert.NotEqual(ProgressionLedger.Empty.ToJson(), learning.Load().Progression.ToJson());
    }

    [Theory]
    [InlineData("progression.json")]
    [InlineData("soreness.json")]
    [InlineData("baselines.json")]
    [InlineData("assessments.json")]
    public void AnUnreadableRecordIsToldAndLeftAlone(string file)
    {
        using var folder = new LearningFolder();
        File.WriteAllText(folder.File(file), "garbage");

        var loaded = new FileWorkoutLearning(folder.Ledger(), folder.Path).Load();

        Assert.NotNull(loaded.UnreadableBecause);
        Assert.Equal("garbage", File.ReadAllText(folder.File(file)));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.unreadable*"));
    }

    [Fact]
    public void SettlingNeverWritesOverAnUnreadableRecord()
    {
        using var folder = new LearningFolder();
        File.WriteAllText(folder.File("progression.json"), "garbage");
        var learning = new FileWorkoutLearning(folder.Ledger(), folder.Path);
        var (document, result) = Resolved.Strength((2, "done"));

        Assert.Throws<WorkoutLearningUnreadableException>(() => learning.Settled(document, result));

        Assert.Equal("garbage", File.ReadAllText(folder.File("progression.json")));
    }

    [Fact]
    public void ABlankStillSoreBoxClearsTheNote()
    {
        using var folder = new LearningFolder();
        File.WriteAllText(folder.File("soreness.json"), SorenessLedger.Empty.Note(Person, "left knee", ReadBackFixture.Day.AddDays(-2)).ToJson());
        var learning = new FileWorkoutLearning(folder.Ledger(), folder.Path);
        var (document, result) = Resolved.WithSoreness("left knee", (2, "done"), (3, "done"));

        learning.Settled(document, result);

        Assert.Empty(learning.Load().Soreness.OpenFor(Person));
    }

    [Fact]
    public void AMarkedStillSoreKeepsTheNote()
    {
        using var folder = new LearningFolder();
        File.WriteAllText(folder.File("soreness.json"), SorenessLedger.Empty.Note(Person, "left knee", ReadBackFixture.Day.AddDays(-2)).ToJson());
        var learning = new FileWorkoutLearning(folder.Ledger(), folder.Path);
        var (document, result) = Resolved.WithSoreness("left knee", (1, WorkoutIds.StillSore), (2, "done"), (3, "done"));

        learning.Settled(document, result);

        Assert.Single(learning.Load().Soreness.OpenFor(Person));
    }

    [Fact]
    public void ADoctorNotePrintedIsKept()
    {
        using var folder = new LearningFolder();
        File.WriteAllText(folder.File("soreness.json"), SorenessLedger.Empty.Note(Person, "left knee", ReadBackFixture.Day).ToJson());
        var learning = new FileWorkoutLearning(folder.Ledger(), folder.Path);

        learning.RecordDoctorNotePrinted(Person);

        Assert.True(new FileWorkoutLearning(folder.Ledger(), folder.Path).Load().Soreness.OpenFor(Person)[0].DoctorNotePrinted);
    }

    [Fact]
    public void AnAssessmentWeekIsKeptPerPerson()
    {
        using var folder = new LearningFolder();
        var learning = new FileWorkoutLearning(folder.Ledger(), folder.Path);

        learning.RecordAssessmentWeek(Person, new DateOnly(2026, 11, 2));
        learning.RecordAssessmentWeek("Robin", new DateOnly(2026, 11, 9));

        var loaded = new FileWorkoutLearning(folder.Ledger(), folder.Path).Load();
        Assert.Equal(new DateOnly(2026, 11, 2), loaded.HistoryOf(Person).AssessmentWeekStart);
        Assert.Equal(new DateOnly(2026, 11, 9), loaded.HistoryOf("Robin").AssessmentWeekStart);
    }

    [Fact]
    public void AnAssessmentDayTeachesTheBaselines()
    {
        using var folder = new LearningFolder();
        var learning = new FileWorkoutLearning(folder.Ledger(), folder.Path);
        var (document, result) = AssessmentDays.Strength(folder, Person, "14", "9");

        learning.Settled(document, result);

        var baselines = new FileWorkoutLearning(folder.Ledger(), folder.Path).Load().BaselinesOf(Person)!;
        Assert.Equal(14m, baselines.Numbers[AssessmentCatalogue.SitToStandId]);
        Assert.Equal(9m, baselines.Numbers[AssessmentCatalogue.WallPushUpsId]);
    }

    [Fact]
    public void ALaterDayKeepsWhatAnEarlierOneTaught()
    {
        using var folder = new LearningFolder();
        var learning = new FileWorkoutLearning(folder.Ledger(), folder.Path);
        learning.Settled(AssessmentDays.Strength(folder, Person, "14", null).Document, AssessmentDays.Strength(folder, Person, "14", null).Result);

        var second = AssessmentDays.Walk(Person, "0.75");
        learning.Settled(second.Document, second.Result);

        var baselines = learning.Load().BaselinesOf(Person)!;
        Assert.Equal(14m, baselines.Numbers[AssessmentCatalogue.SitToStandId]);
        Assert.Equal(0.75m, baselines.Numbers[AssessmentCatalogue.TimedWalkId]);
    }
}
