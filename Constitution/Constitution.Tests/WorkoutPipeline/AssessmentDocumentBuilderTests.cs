using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Documents;
using PlannedTest = Constitution.Domain.Workouts.AssessmentTest;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The first week of a new install is the assessment: three gentle sessions whose pages ask for a number or a circled word.
/// The builder turns each assessment session into its pages and into the boxes the morning read-back will read.
/// </summary>
public class AssessmentDocumentBuilderTests
{
    private static readonly DateTimeOffset Slot = Est(11, 3, 18);

    private static IReadOnlyList<AssessmentSession> Sessions() => AssessmentPlanner.Plan(Request(Calendars(), bothAdults: false)).Sessions;

    private static AssessmentSession Of(PlannedTest test) => Sessions().First(item => item.Test == test);

    private static AssessmentDocumentModel Build(AssessmentSession session, int number = 1, int of = 3, params DateOnly[] others) =>
        AssessmentDocumentBuilder.Build(session, Slot, Zone, number, of, others);

    [Fact]
    public void TheStrengthDayHasOnePagePerCountedTest()
    {
        var model = Build(Of(PlannedTest.StrengthBaselines));

        Assert.Equal(["Sit to stand in 30 seconds", "Wall push-ups in 30 seconds"], model.Tests.Select(test => test.Name));
        Assert.Equal(["stands", "push-ups"], model.Tests.Select(test => test.Unit));
        Assert.All(model.Tests, test => Assert.Null(test.Words));
    }

    [Fact]
    public void TheWalkIsOneNumberInMiles()
    {
        var test = Assert.Single(Build(Of(PlannedTest.TimedWalk)).Tests);

        Assert.Equal("How far in 10 minutes", test.Name);
        Assert.Equal("miles", test.Unit);
        Assert.Contains("10 minutes", test.Instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void TheToeReachIsFourCircledWordsInOrderOfReach()
    {
        var test = Assert.Single(Build(Of(PlannedTest.MobilityCheck)).Tests);

        Assert.Null(test.Unit);
        Assert.Equal(["Knees", "Shins", "Ankles", "Toes"], test.Words);
    }

    [Fact]
    public void EveryTestIsAskedInWordsWithNoFigureAndNoVoice()
    {
        foreach (var session in Sessions())
        {
            var tests = Build(session).Tests;
            Assert.NotEmpty(tests);
            foreach (var test in tests)
            {
                Assert.False(string.IsNullOrWhiteSpace(test.Instruction));
                Assert.False(test.ShowFigure);
                Assert.Null(test.Encouragement);
            }
        }
    }

    [Fact]
    public void TheStrengthTestsSayThirtySeconds()
    {
        var tests = Build(Of(PlannedTest.StrengthBaselines)).Tests;

        Assert.All(tests, test => Assert.Contains("30 seconds", test.Instruction, StringComparison.Ordinal));
    }

    [Fact]
    public void TheCoverCarriesWhichDayOfTheWeekItIs()
    {
        var model = Build(Of(PlannedTest.TimedWalk), number: 2, of: 3);

        Assert.Equal(2, model.Day);
        Assert.Equal(3, model.OfDays);
        Assert.Equal(new DateTime(2026, 11, 3, 18, 0, 0), model.Start);
    }

    [Fact]
    public void TheScheduleNamesTheOtherSessionsByDay()
    {
        var session = Of(PlannedTest.TimedWalk);

        Assert.Equal("The other sessions are on Wednesday and Friday.", Build(session, 1, 3, Day(3), Day(5)).Schedule);
        Assert.Equal("The other session is on Friday.", Build(session, 1, 2, Day(5)).Schedule);
        Assert.Equal("This is the only session this week.", Build(session, 1, 1).Schedule);
    }

    [Fact]
    public void EveryAssessmentDrawsOnItsPages()
    {
        foreach (var session in Sessions())
        {
            var model = Build(session);

            var pdf = ConstitutionDocumentRenderer.Render(AssessmentDocument.Build(model));

            Assert.True(pdf.Length > 0);
        }
    }

    [Fact]
    public void ThePagesAreReadByANumberRuleOrACircleRule()
    {
        var strength = Of(PlannedTest.StrengthBaselines);
        var model = Build(strength);

        var expected = AssessmentDocumentBuilder.Expected(model, strength, Sam, Day(1), Sam);

        Assert.Equal(DeliveredName.For(DeliveryKind.Workout, Day(1), Sam), expected.Name);
        Assert.Equal(DeliveryKind.Workout, expected.Kind);
        Assert.Equal(SessionKind.Strength, expected.Session);
        Assert.Equal([2, 3], expected.Pages.Select(page => page.Number));
        Assert.All(expected.Pages, page => Assert.IsType<NumberRule>(Assert.Single(page.Rules)));
        Assert.All(expected.Pages, page => Assert.Equal([WorkoutIds.Result], page.AskedTargets.Select(target => target.Id)));
        Assert.Equal(["Sit to stand in 30 seconds", "Wall push-ups in 30 seconds"], expected.Pages.Select(page => page.Rules[0].Subject));
    }

    [Fact]
    public void TheToeReachIsReadByTheCircleOfItsFourLanes()
    {
        var mobility = Of(PlannedTest.MobilityCheck);
        var expected = AssessmentDocumentBuilder.Expected(Build(mobility), mobility, Sam, Day(1), Sam);

        var page = Assert.Single(expected.Pages);
        var rule = Assert.IsType<RatingRule>(Assert.Single(page.Rules));
        Assert.Equal(WorkoutIds.Choice, rule.Group);
        Assert.Equal(4, page.AskedTargets.Count);
        Assert.Equal(2, page.Number);
    }

    [Fact]
    public void AnyAnswerOnAnyTestPageClosesTheDayAsDone()
    {
        var strength = Of(PlannedTest.StrengthBaselines);
        var expected = AssessmentDocumentBuilder.Expected(Build(strength), strength, Sam, Day(1), Sam);

        Assert.NotNull(expected.Marks);
        Assert.Contains(WorkoutIds.Result, expected.Marks!.TargetIds);
        var outcome = DayClose.Decide(expected.Marks, [new InkResolution.Written(WorkoutIds.Result, "14")]);
        Assert.Equal(SessionOutcome.Done, outcome);
    }
}
