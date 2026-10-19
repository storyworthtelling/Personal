using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Tests.Documents;
using Constitution.Tests.Ink;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.ReadBack;

/// <summary>A delivered document as the read-back holds it: its name, its pages, and the rules the book wrote for them.</summary>
public class ExpectedDocumentTests
{
    [Fact]
    public void ItsNameIsTheOneDeliveryUploadedUnder()
    {
        var document = Workout(subject: "Assessment");

        Assert.Equal(DeliveredName.For(DeliveryKind.Workout, Day, "Assessment"), document.Name);
    }

    [Fact]
    public void AWorkoutRulesEveryExercisePageAndNotTheCover()
    {
        var document = Workout();

        Assert.Equal([2, 3], document.Pages.Select(page => page.Number));
        Assert.Equal(
            [new ExercisePageRule("Goblet squat", "done", "feel"), new ExercisePageRule("Band row", "done", "feel")],
            document.Pages.SelectMany(page => page.Rules).ToArray());
        Assert.True(document.ReadsInk);
        Assert.Equal(SessionKind.Strength, document.Session);
        Assert.Equal(Person, document.Person);
    }

    [Fact]
    public void AnInjuryDayAsksStillSoreEverywhere()
    {
        var model = WorkoutSamples.InjuryDay(doctor: true);

        var document = ExpectedDocument.ForWorkout(model, Person, SessionKind.Strength, Day);

        Assert.Equal([1, 2, 3, 4], document.Pages.Select(page => page.Number));
        Assert.All(document.Pages, page => Assert.Contains(page.Rules, rule => rule is StillSoreRule { BoxId: "sore" }));
        Assert.Equal([new StillSoreRule("left knee", "sore")], document.Pages[0].Rules.ToArray());
    }

    [Fact]
    public void ASessionIsTheTickAndTheRatingOnly()
    {
        var injured = ExpectedDocument.ForWorkout(WorkoutSamples.InjuryDay(), Person, SessionKind.Strength, Day);

        Assert.Equal(["done", "feel"], injured.Marks!.TargetIds);
        Assert.Equal(Person, injured.Marks.Person);
        Assert.Equal(Day, injured.Marks.Day);
        Assert.Equal(SessionKind.Strength, injured.Marks.Kind);
    }

    [Fact]
    public void TheRulesNameOnlyBoxesThePagesHave()
    {
        foreach (var document in new[] { Workout(), ExpectedDocument.ForWorkout(WorkoutSamples.InjuryDay(true), Person, SessionKind.Strength, Day) })
        {
            foreach (var page in document.Pages)
            {
                var read = InkPageRead.Read(ReadBackFixture.EmptyBoxes(page));
                var resolutions = new InkResolver(page.Readout).Resolve(read, page.Rules, Morning);

                Assert.NotEmpty(resolutions);
                Assert.DoesNotContain(resolutions, resolution => resolution is InkResolution.NotRead);
            }
        }
    }

    [Fact]
    public void ThePagesAskedBoxesLeaveTheNotesMarginAlone()
    {
        var page = Workout().Pages[0];

        Assert.Equal(["feel-1", "feel-2", "feel-3", "done"], page.AskedTargets.Select(target => target.Id));
        Assert.DoesNotContain(page.AskedTargets, target => target.Kind == InkTargetKind.NotesMargin);
    }

    [Fact]
    public void AWorkoutNeedsASessionKind()
    {
        var page = Workout().Pages[0];

        Assert.Throws<ArgumentException>(() => new ExpectedDocument(DeliveryKind.Workout, Day, null, Person, null, [page]));
    }

    [Fact]
    public void ADigestHoldsNoPagesAndNoSession()
    {
        var page = Workout().Pages[0];
        var digest = Digest();

        Assert.False(digest.ReadsInk);
        Assert.Null(digest.Marks);
        Assert.Throws<ArgumentException>(() => new ExpectedDocument(DeliveryKind.Digest, Day, null, Person, null, [page]));
    }

    [Fact]
    public void AMealsDocumentHasNoSessionAndNeedsAPage()
    {
        var meals = Meals();

        Assert.Null(meals.Marks);
        Assert.True(meals.ReadsInk);
        Assert.Throws<ArgumentException>(() => new ExpectedDocument(DeliveryKind.Meals, Day, null, Person, SessionKind.Strength, meals.Pages));
        Assert.Throws<ArgumentException>(() => new ExpectedDocument(DeliveryKind.Meals, Day, null, Person, null, []));
    }

    [Fact]
    public void ADocumentBelongsToSomeone()
    {
        var pages = Workout().Pages;

        Assert.Throws<ArgumentException>(() => new ExpectedDocument(DeliveryKind.Workout, Day, null, " ", SessionKind.Strength, pages));
    }

    [Fact]
    public void TwoPagesCannotShareANumber()
    {
        var page = Workout().Pages[0];

        Assert.Throws<ArgumentException>(() => new ExpectedDocument(DeliveryKind.Workout, Day, null, Person, SessionKind.Strength, [page, page]));
    }

    [Fact]
    public void PagesCountFromOne()
    {
        var map = Workout().Pages[0].Readout;

        Assert.Throws<ArgumentOutOfRangeException>(() => new ExpectedPage(0, map, [new WorkoutBoxRule("x", "done")]));
    }

    [Fact]
    public void AWorkoutWithNoBoxToTickCannotBeRead()
    {
        // The cover alone, on an injury day, holds only the soreness question.
        var cover = ExpectedDocument.ForWorkout(WorkoutSamples.InjuryDay(), Person, SessionKind.Strength, Day).Pages[0];

        Assert.Throws<ArgumentException>(() => new ExpectedDocument(DeliveryKind.Workout, Day, null, Person, SessionKind.Strength, [cover]));
    }

    [Fact]
    public void ARuleCannotBeNullOrAbsentFromAPage()
    {
        var readout = new InkFixture().Readout;

        Assert.Throws<ArgumentNullException>(() => new ExpectedPage(1, readout, null!));
        Assert.Throws<ArgumentNullException>(() => new ExpectedPage(1, null!, []));
    }
}
