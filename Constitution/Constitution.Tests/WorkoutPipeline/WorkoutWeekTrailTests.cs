using Constitution.Domain.Calendar;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The weekly run's lines in the Body trail. Every phrase must come through the export's own filter whole (a withheld line
/// is a morning the owner's device test cannot read), and none may hold a name, a title, a date or an exception message.
/// </summary>
public class WorkoutWeekTrailTests
{
    private const string Stamp = "[Nov 4, 6:45 am] INFO Body — ";
    private const string Title = "Constitution — Workout — 2026-11-03 — Sam";

    private static WorkoutWeekOutcome.Planned Planned(int created = 3, int failed = 1, int unreadablePeople = 0, params DocumentOutcome[] documents)
    {
        var strength = PlannedAt(0, (16, 0), (18, 0), (16, 0), Effort(40), Together.None, Sam);
        var events = Enumerable.Range(0, created).Select(number => new EventToCreate(StrengthKey(Sam, number), Sam, "Workout · 40 min", At(0, 16), At(0, 17))).ToList();
        var refused = Enumerable.Range(0, failed).Select(number => new CreateFailure(new EventToCreate(StrengthKey(Lee, number), Lee, "Workout · 40 min", At(1, 16), At(1, 17)), "No.")).ToList();
        var run = new PlacementRun(new PlacementPlan([]), events, refused, null);
        var people = Enumerable.Range(0, unreadablePeople).Select(_ => Lee).ToList();
        return new WorkoutWeekOutcome.Planned(new WorkoutWeekReport(
            Request(Calendars()),
            PlanOf(strength), PlanOf(strength), new Dictionary<string, PhaseDecision>(), null, [], [], [], true, [], [], people, run, documents));
    }

    public static TheoryData<DocumentOutcome> EveryDocumentOutcome()
    {
        var day = new DateOnly(2026, 11, 3);
        var data = new TheoryData<DocumentOutcome>();
        foreach (var verdict in Enum.GetValues<DocumentVerdict>())
        {
            var failure = verdict is DocumentVerdict.NotBuilt or DocumentVerdict.NotDelivered
                ? new HttpRequestException("The tablet said no to Sam's page at 18:00.")
                : null;
            data.Add(new DocumentOutcome(Title, Sam, day, verdict, failure));
        }

        return data;
    }

    public static TheoryData<WorkoutWeekOutcome> EveryOutcome()
    {
        var data = new TheoryData<WorkoutWeekOutcome>();
        foreach (var because in Enum.GetValues<NotPlannedBecause>())
        {
            data.Add(new WorkoutWeekOutcome.NotPlanned(because, [new UnreadableCalendar(CalendarSource.Own, "Lee has not shared a calendar.")], "Lee has not shared a calendar."));
        }

        data.Add(Planned());
        data.Add(Planned(0, 0));
        data.Add(Planned(12, 0, 2));
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryDocumentOutcome))]
    public void DocumentPhrasesSurviveTheFilter(DocumentOutcome outcome)
    {
        var line = Stamp + WorkoutWeekTrail.LogPhrase(outcome);

        Assert.Equal(line, TrailLines.Filter(line));
    }

    [Theory]
    [MemberData(nameof(EveryOutcome))]
    public void EveryRunPhraseSurvivesTheExportFilterWhole(WorkoutWeekOutcome outcome)
    {
        var line = Stamp + WorkoutWeekTrail.LogPhrase(outcome);

        Assert.Equal(line, TrailLines.Filter(line));
    }

    [Theory]
    [MemberData(nameof(EveryDocumentOutcome))]
    public void NoDocumentPhraseHoldsAnyContent(DocumentOutcome outcome)
    {
        var phrase = WorkoutWeekTrail.LogPhrase(outcome);

        Assert.StartsWith("workout document", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("Constitution", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("Sam", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("2026", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("18:00", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("said no", phrase, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryOutcome))]
    public void NoRunPhraseHoldsANameADateOrAMessage(WorkoutWeekOutcome outcome)
    {
        var phrase = WorkoutWeekTrail.LogPhrase(outcome);

        Assert.StartsWith("workout week", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("Lee", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("shared", phrase, StringComparison.Ordinal);
    }

    [Fact]
    public void APlannedWeekCarriesOnlyCounts()
    {
        Assert.Equal("workout week planned, 1 sessions, 3 events written, 1 events failed, 0 people unreadable", WorkoutWeekTrail.LogPhrase(Planned()));
        Assert.Equal("workout week planned, 1 sessions, 12 events written, 0 events failed, 2 people unreadable", WorkoutWeekTrail.LogPhrase(Planned(12, 0, 2)));
    }

    [Fact]
    public void ANotPlannedWeekSaysWhyInAWord()
    {
        var unreadable = new List<UnreadableCalendar> { new(CalendarSource.Own, "x") };

        Assert.Equal("workout week not planned, records unreadable", WorkoutWeekTrail.LogPhrase(new WorkoutWeekOutcome.NotPlanned(NotPlannedBecause.RecordsUnreadable, [], "x")));
        Assert.Equal("workout week not planned, calendars unreadable", WorkoutWeekTrail.LogPhrase(new WorkoutWeekOutcome.NotPlanned(NotPlannedBecause.CalendarsUnreadable, unreadable, "x")));
        Assert.Equal("workout week not planned, no one to plan", WorkoutWeekTrail.LogPhrase(new WorkoutWeekOutcome.NotPlanned(NotPlannedBecause.NoOneToPlan, [], "x")));
    }

    [Fact]
    public void AFailurePhraseNamesTheStepsExceptionTypeOnly()
    {
        var built = WorkoutWeekTrail.LogPhrase(new DocumentOutcome(Title, Sam, new DateOnly(2026, 11, 3), DocumentVerdict.NotBuilt, new ArgumentException("C:\\private\\path")));
        var refused = WorkoutWeekTrail.LogPhrase(new DocumentOutcome(Title, Sam, new DateOnly(2026, 11, 3), DocumentVerdict.NotDelivered, new HttpRequestException("503")));

        Assert.Equal("workout document not built: ArgumentException", built);
        Assert.Equal("workout document not delivered: HttpRequestException, will try again", refused);
    }

    [Fact]
    public void AFailureWithNoExceptionStillMakesAPhrase()
    {
        var line = WorkoutWeekTrail.LogPhrase(new DocumentOutcome(Title, Sam, new DateOnly(2026, 11, 3), DocumentVerdict.NotDelivered));

        Assert.StartsWith("workout document not delivered", line, StringComparison.Ordinal);
        Assert.Equal(Stamp + line, TrailLines.Filter(Stamp + line));
    }

    [Theory]
    [InlineData(PlanOrigin.Authored, null)]
    [InlineData(PlanOrigin.RulesOnly, AuthorUnavailable.NotConfigured)]
    [InlineData(PlanOrigin.RulesOnly, AuthorUnavailable.Offline)]
    [InlineData(PlanOrigin.RulesOnly, AuthorUnavailable.TimedOut)]
    [InlineData(PlanOrigin.RulesOnly, AuthorUnavailable.KeyRejected)]
    [InlineData(PlanOrigin.RulesOnly, AuthorUnavailable.ReplyUnusable)]
    [InlineData(PlanOrigin.RulesOnly, AuthorUnavailable.BrokeTheRules)]
    public void ThePlanOriginLineGoesThroughTheTrailWhole(PlanOrigin origin, AuthorUnavailable? reason)
    {
        var log = new RecordingWeekLog();

        new TrailPlanLog(log).Record(origin, reason);

        var phrase = Assert.Single(log.Phrases);
        Assert.Equal(PlanDiagnostics.Line(origin, reason).ToLowerInvariant(), phrase);
        Assert.Equal(Stamp + phrase, TrailLines.Filter(Stamp + phrase));
    }
}
