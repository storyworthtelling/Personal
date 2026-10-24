using System.Text.Json;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Tests.Documents;
using Constitution.Tests.ReadBack;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// The progression read against pages the way the printed document really has them (review finding B1). Every exercise
/// page prints its Done tick and its easy / right / hard choice under the same two ids, so the answers can only be told
/// apart by the page they came from. These tests print the real document, put ink on named pages, resolve it with the real
/// resolver and settle the real result; none of them may invent an id the painter never prints.
/// </summary>
public class ProgressionRealPagesTests
{
    private static readonly WorkoutDocumentModel Model =
        WorkoutSamples.Workout(exercises: [WorkoutSamples.Squat(), WorkoutSamples.Row(), WorkoutSamples.Walk()]);

    private static readonly Exercise[] Moves = Model.Exercises.Select(page => new Exercise(page.Name)).ToArray();

    private static ExpectedDocument Printed(int dayOffset = 0) =>
        ExpectedDocument.ForWorkout(Model, Sam, SessionKind.Strength, Day(dayOffset));

    private static SessionPages Session(int dayOffset = 0, string? person = null) =>
        new(person ?? Sam, Day(dayOffset), SessionKind.Strength, Moves.Select(move => new ExercisePageRef(move)).ToList());

    // A mark is a page number and either "done" or one of the words of the choice: the lane that carries the word is found on the printed page.
    private static IReadOnlyList<PageResolutions> Read(ExpectedDocument document, params (int Page, string Mark)[] marks)
    {
        var targets = marks.Select(mark => (mark.Page, Target: TargetOf(document, mark.Page, mark.Mark))).ToArray();
        var read = ReadBackFixture.Read(document, PenStrokes.Seen, targets);
        return document.Pages
            .Select(page => new PageResolutions(page.Number, new InkResolver(page.Readout).Resolve(read.PageFor(page.Number), page.Rules, ReadBackFixture.Morning)))
            .ToList();
    }

    private static string TargetOf(ExpectedDocument document, int page, string mark)
    {
        var map = document.Pages.Single(item => item.Number == page).Readout.Map;
        return mark == WorkoutIds.Done
            ? WorkoutIds.Done
            : map.Targets.Single(target => target.Kind == InkTargetKind.CircleLane && target.Group == WorkoutIds.Rating && target.Meaning == mark).Id;
    }

    [Fact]
    public void EachPageAnswersForItsOwnExercise()
    {
        var pages = Read(Printed(), (2, "done"), (2, "easy"), (3, "done"), (4, "done"));
        var ledger = ProgressionLedger.Empty
            .Settle(Session(0), pages)
            .Settle(Session(1), pages);

        // Only the first page was rated easy, and twice running moves that one exercise.
        Assert.NotEqual(ProgressionLedger.Empty.DoseFor(Sam, Moves[0]), ledger.DoseFor(Sam, Moves[0]));
        Assert.Equal(ProgressionLedger.Empty.DoseFor(Sam, Moves[1]), ledger.DoseFor(Sam, Moves[1]));
        Assert.Equal(ProgressionLedger.Empty.DoseFor(Sam, Moves[2]), ledger.DoseFor(Sam, Moves[2]));
    }

    [Fact]
    public void OnlyTheFirstPageTickedIsOnlyTheFirstDone()
    {
        var pages = Read(Printed(), (2, "done"), (2, "easy"));

        var reads = PageReading.Read(Session(), pages);

        Assert.Equal([PageVerdict.Done, PageVerdict.NotDone, PageVerdict.NotDone], reads.Select(item => item.Verdict).ToArray());
        Assert.Equal<ExerciseRating?>([ExerciseRating.Easy, null, null], reads.Select(item => item.Rating).ToArray());
    }

    [Fact]
    public void EachPagesRatingStaysOnItsPage()
    {
        var pages = Read(Printed(), (2, "done"), (2, "hard"), (3, "done"), (3, "easy"), (4, "done"), (4, "right"));

        var reads = PageReading.Read(Session(), pages);

        Assert.Equal<ExerciseRating?>([ExerciseRating.Hard, ExerciseRating.Easy, ExerciseRating.Right], reads.Select(item => item.Rating).ToArray());
    }

    [Fact]
    public void TheCoverIsNotAnExercise()
    {
        var onlyTheCover = new[] { new PageResolutions(1, [new InkResolution.Ticked(WorkoutIds.Done), new InkResolution.Circled(WorkoutIds.Rating, "easy")]) };

        var reads = PageReading.Read(Session(), onlyTheCover);

        Assert.All(reads, item => Assert.Equal(PageVerdict.Unknown, item.Verdict));
    }

    [Fact]
    public void APageTheResultNeverReachedIsUnknown()
    {
        var pages = Read(Printed(), (2, "done"), (3, "done")).Where(page => page.Number != 4).ToList();

        var reads = PageReading.Read(Session(), pages);

        Assert.Equal([PageVerdict.Done, PageVerdict.Done, PageVerdict.Unknown], reads.Select(item => item.Verdict).ToArray());
    }

    [Fact]
    public void AReReadOfTheSameDayCountsOnce()
    {
        var pages = Read(Printed(), (2, "done"), (2, "easy"), (3, "done"), (4, "done"));
        var once = ProgressionLedger.Empty.Settle(Session(0), pages);

        var again = once.Settle(Session(0), pages);

        Assert.Equal(once.DoseFor(Sam, Moves[0]), again.DoseFor(Sam, Moves[0]));
        Assert.Equal(ProgressionLedger.Empty.DoseFor(Sam, Moves[0]), again.DoseFor(Sam, Moves[0]));
        Assert.Equal(once.ToJson(), again.ToJson());
    }

    [Fact]
    public void TwoReadsOfOneDayAreNotTwoEasyRuns()
    {
        var pages = Read(Printed(), (2, "done"), (2, "easy"), (3, "done"), (4, "done"));

        var ledger = ProgressionLedger.Empty.Settle(Session(0), pages).Settle(Session(0), pages);

        // One easy day has not yet earned a step up.
        Assert.Equal(ProgressionLedger.Empty.DoseFor(Sam, Moves[0]), ledger.DoseFor(Sam, Moves[0]));
    }

    [Fact]
    public void NothingReadDoesNotUseTheDayUp()
    {
        var nothing = new[] { new PageResolutions(2, [new InkResolution.NotRead(WorkoutIds.Done, "not read")]) };
        var pages = Read(Printed(), (2, "done"), (2, "easy"), (3, "done"), (4, "done"));

        var ledger = ProgressionLedger.Empty.Settle(Session(0), nothing);
        var later = ledger.Settle(Session(0), pages).Settle(Session(1), pages);

        Assert.Equal(ProgressionLedger.Empty.ToJson(), ledger.ToJson());
        Assert.NotEqual(ProgressionLedger.Empty.DoseFor(Sam, Moves[0]), later.DoseFor(Sam, Moves[0]));
    }

    [Fact]
    public void TwoDifferentDaysAreTwoRuns()
    {
        var pages = Read(Printed(), (2, "done"), (2, "easy"), (3, "done"), (4, "done"));

        var ledger = ProgressionLedger.Empty.Settle(Session(0), pages).Settle(Session(1), pages);

        Assert.NotEqual(ProgressionLedger.Empty.DoseFor(Sam, Moves[0]), ledger.DoseFor(Sam, Moves[0]));
    }

    [Fact]
    public void TwoPeopleOnOneDayAreBothSettled()
    {
        var pages = Read(Printed(), (2, "done"), (2, "easy"), (3, "done"), (4, "done"));

        var ledger = ProgressionLedger.Empty.Settle(Session(0), pages).Settle(Session(0, Lee), pages).Settle(Session(1), pages).Settle(Session(1, Lee), pages);

        Assert.NotEqual(ProgressionLedger.Empty.DoseFor(Lee, Moves[0]), ledger.DoseFor(Lee, Moves[0]));
        Assert.Equal(ledger.DoseFor(Sam, Moves[0]), ledger.DoseFor(Lee, Moves[0]));
    }

    [Fact]
    public void ThePersonIsMatchedWithoutCaseOrSpaces()
    {
        var pages = Read(Printed(), (2, "done"), (2, "easy"), (3, "done"), (4, "done"));

        var ledger = ProgressionLedger.Empty.Settle(Session(0), pages).Settle(Session(0, " sam "), pages);

        Assert.Equal(ProgressionLedger.Empty.DoseFor(Sam, Moves[0]), ledger.DoseFor(Sam, Moves[0]));
    }

    [Fact]
    public void TheSettledDaysSurviveTheFile()
    {
        var pages = Read(Printed(), (2, "done"), (2, "easy"), (3, "done"), (4, "done"));
        var stored = ProgressionLedger.Empty.Settle(Session(0), pages).ToJson();

        var restored = ProgressionLedger.FromJson(stored);
        var again = restored.Ledger.Settle(Session(0), pages);

        Assert.False(restored.WholeFileUnreadable);
        Assert.Equal(0, restored.UnreadableEntries);
        Assert.Equal(stored, again.ToJson());
    }

    [Fact]
    public void AFileFromBeforeSettledDaysStillReads()
    {
        var older = "{\"version\":1,\"exercises\":[],\"caps\":[]}";

        var read = ProgressionLedger.FromJson(older);

        Assert.False(read.WholeFileUnreadable);
        Assert.Equal(0, read.UnreadableEntries);
    }

    [Fact]
    public void ASettledDayThatCannotBeReadIsCounted()
    {
        var json = "{\"version\":1,\"exercises\":[],\"caps\":[],\"settled\":[{\"person\":\"Sam\",\"day\":\"not a day\"},{\"person\":\"Sam\",\"day\":\"2026-10-20\"}]}";

        var read = ProgressionLedger.FromJson(json);

        Assert.False(read.WholeFileUnreadable);
        Assert.Equal(1, read.UnreadableEntries);
    }

    [Fact]
    public void TheSettledNamesAreLiteralsAndPinned()
    {
        var json = ProgressionLedger.Empty.Settle(Session(0), Read(Printed(), (2, "done"))).ToJson();

        using var document = JsonDocument.Parse(json);
        var entry = Assert.Single(document.RootElement.GetProperty("settled").EnumerateArray());
        Assert.Equal(["day", "person"], entry.EnumerateObject().Select(item => item.Name).Order().ToArray());
        Assert.Equal(Day(0).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), entry.GetProperty("day").GetString());
    }

    [Fact]
    public void OldSettledDaysAreLetGo()
    {
        var pages = Read(Printed(), (2, "done"));
        var ledger = ProgressionLedger.Empty.Settle(Session(0), pages).Settle(Session(60), pages);

        using var document = JsonDocument.Parse(ledger.ToJson());

        Assert.Single(document.RootElement.GetProperty("settled").EnumerateArray());
    }

    // ---- the older, flat way in ----

    [Fact]
    public void AFlatAnswerForAOnePageSessionStillSettles()
    {
        var single = new SessionPages(Sam, Day(0), SessionKind.Strength, [new ExercisePageRef(Moves[0])]);

        var ledger = ProgressionLedger.Empty
            .Settle(single, [new InkResolution.Ticked(WorkoutIds.Done), new InkResolution.Circled(WorkoutIds.Rating, "hard")]);

        Assert.True(ledger.ToJson().Contains("\"easyRun\":0", StringComparison.Ordinal));
        Assert.Equal(PageVerdict.Done, PageReading.Read(single, [new InkResolution.Ticked(WorkoutIds.Done)]).Single().Verdict);
    }

    [Fact]
    public void AFlatAnswerSharedByManyPagesIsRefused()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            ProgressionLedger.Empty.Settle(Session(0), [new InkResolution.Ticked(WorkoutIds.Done)]));

        Assert.Contains("which page", error.Message, StringComparison.Ordinal);
    }
}
