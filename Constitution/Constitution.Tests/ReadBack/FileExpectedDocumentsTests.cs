using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.ReadBack;
using Constitution.Tests.Documents;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// The delivered documents waiting to be read, kept across app restarts and process deaths. What comes back must be
/// the same boxes and the same rules, because the read-back would otherwise ask the vision read about the wrong place.
/// </summary>
public sealed class FileExpectedDocumentsTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-expected-");

    private string PathOf() => Path.Combine(folder.FullName, "nested", "expected.json");

    private FileExpectedDocuments Make() => new(PathOf());

    public void Dispose() => folder.Delete(recursive: true);

    private static void AssertSame(ExpectedDocument expected, ExpectedDocument actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Day, actual.Day);
        Assert.Equal(expected.Subject, actual.Subject);
        Assert.Equal(expected.Person, actual.Person);
        Assert.Equal(expected.Session, actual.Session);
        Assert.Equal(expected.Pages.Select(page => page.Number), actual.Pages.Select(page => page.Number));
        for (var index = 0; index < expected.Pages.Count; index++)
        {
            Assert.Equal(expected.Pages[index].Rules, actual.Pages[index].Rules);
            Assert.Equal(expected.Pages[index].Readout.Map.Targets, actual.Pages[index].Readout.Map.Targets);
            Assert.Equal(expected.Pages[index].AskedTargets, actual.Pages[index].AskedTargets);
        }

        Assert.Equal(expected.Marks?.TargetIds, actual.Marks?.TargetIds);
    }

    [Fact]
    public void NothingRecordedIsNothingOutstanding() => Assert.Empty(Make().Outstanding());

    [Fact]
    public void AWorkoutComesBackWithTheSameBoxesAndRules()
    {
        var workout = Workout();
        Make().Record(workout);

        var back = Assert.Single(Make().Outstanding());

        Assert.False(back.Saved);
        AssertSame(workout, back.Document);
    }

    [Fact]
    public void AnInjuryDayComesBackWithItsSorenessRules()
    {
        var injured = ExpectedDocument.ForWorkout(WorkoutSamples.InjuryDay(true), Person, SessionKind.Strength, Day, "Gentle");
        Make().Record(injured);

        AssertSame(injured, Assert.Single(Make().Outstanding()).Document);
    }

    [Fact]
    public void EveryKindOfRuleSurvivesTheFile()
    {
        var fixture = new Constitution.Tests.Ink.InkFixture();
        var rules = new InkRule[]
        {
            new WorkoutBoxRule("push-ups", "pushups"),
            new ExercisePageRule("push-ups", "done", "feel"),
            new MealItemRule("lentil soup", "dinner-1", "dinner-no", new DateTimeOffset(2026, 3, 14, 17, 0, 0, TimeSpan.FromHours(-4))),
            new RatingRule("push-ups", "feel"),
            new StillSoreRule("the sore knee", "sore"),
            new StapleRule("olive oil", "oil"),
            new NumberRule("toe reach", "reach"),
        };
        var meals = new ExpectedDocument(DeliveryKind.Meals, Day, null, Person, null, [new ExpectedPage(1, fixture.Readout, rules)]);
        Make().Record(meals);

        var back = Assert.Single(Make().Outstanding()).Document;

        Assert.Equal(rules, back.Pages[0].Rules);
        Assert.Equal(((MealItemRule)rules[2]).Deadline, ((MealItemRule)back.Pages[0].Rules[2]).Deadline);
        Assert.Equal(((MealItemRule)rules[2]).Deadline.Offset, ((MealItemRule)back.Pages[0].Rules[2]).Deadline.Offset);
    }

    [Fact]
    public void EveryRuleTypeIsCoveredByThatTest()
    {
        var made = typeof(InkRule).Assembly.GetTypes().Where(type => type.BaseType == typeof(InkRule)).Select(type => type.Name).Order();

        Assert.Equal(
            ["ExercisePageRule", "MealItemRule", "NumberRule", "RatingRule", "StapleRule", "StillSoreRule", "WorkoutBoxRule"],
            made);
    }

    [Fact]
    public void TargetUnitsAndGroupsSurviveTheFile()
    {
        var layouts = AssessmentDocument.Build(WorkoutSamples.Assessment());
        var readout = new InkReadout(layouts[4].Map);
        var assessment = new ExpectedDocument(
            DeliveryKind.Workout, Day, "Assessment", Person, SessionKind.Mobility,
            [new ExpectedPage(5, readout, [new RatingRule("Toe reach", "choice")])]);
        var withNumber = new ExpectedDocument(
            DeliveryKind.Workout, Day, "Assessment two", Person, SessionKind.Strength,
            [new ExpectedPage(2, new InkReadout(layouts[1].Map), [new NumberRule("Push-ups in a row", "result")])]);
        Make().Record(assessment);
        Make().Record(withNumber);

        var back = Make().Outstanding().Select(item => item.Document).OrderBy(document => document.Subject).ToList();

        AssertSame(assessment, back[0]);
        AssertSame(withNumber, back[1]);
        Assert.Equal("reps", back[1].Pages[0].AskedTargets.Single().Unit);
    }

    [Fact]
    public void TheDigestAndTheMealsDocumentComeBack()
    {
        Make().Record(Digest());
        Make().Record(Meals());

        var names = Make().Outstanding().Select(item => item.Document.Name).Order().ToList();

        Assert.Equal([Digest().Name, Meals().Name], names.Order());
        Assert.False(Make().Outstanding().Single(item => item.Document.Name == Digest().Name).Document.ReadsInk);
    }

    [Fact]
    public void RecordingTheSameNameAgainReplacesIt()
    {
        var store = Make();
        store.Record(Workout());
        store.MarkSaved(Workout().Name);

        store.Record(Workout());

        var only = Assert.Single(store.Outstanding());
        Assert.False(only.Saved);
    }

    [Fact]
    public void MarkingSavedIsRememberedAndRetiringRemoves()
    {
        var store = Make();
        store.Record(Workout());
        store.Record(Digest());

        store.MarkSaved(Workout().Name);

        Assert.True(Make().Outstanding().Single(item => item.Document.Name == Workout().Name).Saved);
        Assert.False(Make().Outstanding().Single(item => item.Document.Name == Digest().Name).Saved);

        store.Retire(Workout().Name);

        Assert.Equal(Digest().Name, Assert.Single(Make().Outstanding()).Document.Name);
    }

    [Fact]
    public void MarkingOrRetiringSomethingNotThereIsHarmless()
    {
        var store = Make();
        store.Record(Workout());

        store.MarkSaved("not there");
        store.Retire("not there");

        Assert.Single(store.Outstanding());
    }

    [Fact]
    public void ADamagedFileIsSetAsideAndTheStoreCarriesOnEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{ torn");

        Assert.Empty(Make().Outstanding());

        Assert.False(File.Exists(PathOf()));
        Assert.Equal("{ torn", File.ReadAllText(PathOf() + ".unreadable"));
        Make().Record(Workout());
        Assert.Single(Make().Outstanding());
    }

    [Fact]
    public void ARuleFromANewerVersionSetsTheFileAside()
    {
        Make().Record(Workout());
        File.WriteAllText(PathOf(), File.ReadAllText(PathOf()).Replace("exercise-page", "levitation", StringComparison.Ordinal));

        Assert.Empty(Make().Outstanding());
        Assert.True(File.Exists(PathOf() + ".unreadable"));
    }

    [Fact]
    public void TheFileHoldsTitlesAndBoxesNotTheOwnersWords()
    {
        Make().Record(Workout());

        var text = File.ReadAllText(PathOf());

        Assert.DoesNotContain("Five more pounds", text, StringComparison.Ordinal);
        Assert.DoesNotContain("kettlebell", text, StringComparison.Ordinal);
    }
}
