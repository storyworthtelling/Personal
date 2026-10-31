using System.Text.Json;
using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Voice;
using Constitution.Domain.Workouts;
using WorkoutIds = Constitution.Domain.Documents.WorkoutIds;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// Plans written by hand for the progression, soreness and recovery tests: a session is exactly the exercises, day and
/// hour the test names, so an expectation never depends on what the planner happened to choose that week. (The planner's
/// own choices are pinned by <c>WorkoutPlannerTests</c>.) Everyone is invented.
/// </summary>
internal static class HandmadePlans
{
    public static PlannedSession Placed(
        int dayOffset,
        SessionKind kind,
        string[] people,
        Together together = Together.None,
        Exercise[]? exercises = null,
        int hour = 17,
        int minutes = 40)
    {
        var gap = new FreeGap(At(dayOffset, hour), At(dayOffset, hour + 1));
        var sweaty = kind is SessionKind.OutdoorCardio or SessionKind.Treadmill;
        var session = new Session(kind, TimeSpan.FromMinutes(minutes), sweaty, exercises ?? DefaultExercises(kind));
        return new PlannedSession(Day(dayOffset), gap.Start, gap, session, people, together);
    }

    public static Exercise[] DefaultExercises(SessionKind kind) => kind switch
    {
        SessionKind.Strength =>
        [
            new("Sit to stand from a chair"),
            new("Push-ups against a wall"),
            new("Wall angels"),
            new("Hip hinge with hands on hips"),
            new("Standing knee lift"),
        ],
        SessionKind.OutdoorCardio => [new("Warm-up walk"), new("Brisk walk"), new("Cool-down walk")],
        SessionKind.Treadmill => [new("Warm-up walk on the treadmill", EquipmentCatalogue.TreadmillId), new("Steady brisk walk on the treadmill", EquipmentCatalogue.TreadmillId)],
        SessionKind.Mobility => [new("Neck and shoulder rolls"), new("Standing hip circles"), new("Doorway chest stretch"), new("Calf stretch against a wall")],
        _ => [new("Easy walk")],
    };

    /// <summary>One week for Sam and Lee holding exactly these sessions; a joint session is in both plans and in the joint list.</summary>
    public static HouseholdWorkoutPlan Week(IEnumerable<PlannedSession> sam, IEnumerable<PlannedSession>? lee = null, IEnumerable<PlannedSession>? joint = null)
    {
        var jointList = (joint ?? []).ToList();
        var samList = sam.Concat(jointList).OrderBy(item => item.Start).ToList();
        var leeList = (lee ?? []).Concat(jointList).OrderBy(item => item.Start).ToList();
        return new HouseholdWorkoutPlan(
            WeekStart,
            [new WeeklyPlan(Sam, WeekStart, samList, null), new WeeklyPlan(Lee, WeekStart, leeList, null)],
            jointList);
    }

    public static PlannedSession OnDay(HouseholdWorkoutPlan plan, string person, int dayOffset) =>
        plan.PlanFor(person)!.Sessions.Single(item => item.Day == Day(dayOffset));

    public static PlannedSession? MaybeOnDay(HouseholdWorkoutPlan plan, string person, int dayOffset) =>
        plan.PlanFor(person)!.Sessions.SingleOrDefault(item => item.Day == Day(dayOffset));
}

/// <summary>
/// C-5.1.3 · Progression from the pages. A ticked page was done as written; "hard" on an exercise, or a session stopped
/// early, eases it; "easy" twice running moves the load up, and the encouragement seat is handed the news.
///
/// <para>The ink is built the way the resolver reports it (a tick and a circled word, or a circled word alone), so the
/// tests stand on the committed <c>InkResolution</c> shapes and not on a private idea of them.</para>
/// </summary>
public class ProgressionTests
{
    private static readonly Exercise Goblet = new("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId);
    private static readonly Exercise Row = new("One-arm weight row", EquipmentCatalogue.DumbbellsId);
    private static readonly Exercise Wall = new("Push-ups against a wall");
    private static readonly Exercise Hinge = new("Hip hinge with hands on hips");
    private static readonly Exercise Bridge = new("Glute bridge on the mat", EquipmentCatalogue.MatId);

    private static SessionPages Pages(int day, params Exercise[] exercises) => Pages(Sam, SessionKind.Strength, day, exercises);

    // The ids are the ones the real exercise pages print (WorkoutIds), the same on every page, so what tells the pages apart
    // is their number in the document, as it is in a real read-back. A fixture with an id per page would hide the very
    // fault this suite exists to catch.
    private static SessionPages Pages(string person, SessionKind kind, int day, params Exercise[] exercises) =>
        new(person, Day(day), kind, exercises.Select(exercise => new ExercisePageRef(exercise)).ToList());

    // Exercise n of the session is page n + 1 of the document (page 1 is the cover).
    private static PageResolutions[] OnPage(int exercise, params InkResolution[] resolutions) => [new PageResolutions(exercise + 1, resolutions)];

    private static PageResolutions[] Rated(int page, string word) => OnPage(page, new InkResolution.Ticked(WorkoutIds.Done), new InkResolution.Circled(WorkoutIds.Rating, word));

    private static PageResolutions[] TickedOnly(int page) => OnPage(page, new InkResolution.Ticked(WorkoutIds.Done), new InkResolution.Unchanged(WorkoutIds.Rating));

    private static PageResolutions[] Empty(int page) => OnPage(page, new InkResolution.Skipped(WorkoutIds.Done), new InkResolution.Unchanged(WorkoutIds.Rating));

    private static PageResolutions[] CircledAlone(int page, string word) => OnPage(page, new InkResolution.PageDone(WorkoutIds.Done, word));

    private static PageResolutions[] Unread(int page) =>
        OnPage(page, new InkResolution.NotRead(WorkoutIds.Done, "page not read"), new InkResolution.NotRead(WorkoutIds.Rating, "page not read"));

    private static PageResolutions[] All(params PageResolutions[][] pages) => pages.SelectMany(page => page).ToArray();

    private static ProgressionLedger Climbed(params Exercise[] exercises)
    {
        // Two easy sessions: every exercise has moved up exactly once.
        var ledger = ProgressionLedger.Empty;
        for (var session = 0; session < 2; session++)
        {
            ledger = ledger.Settle(Pages(session, exercises), All(exercises.Select((_, index) => Rated(index + 1, "easy")).ToArray()));
        }

        return ledger;
    }

    // ---- HardEasesThatExercise ----

    [Fact]
    public void HardEasesThatExercise()
    {
        var ledger = Climbed(Goblet, Wall, Row);
        Assert.Equal(new ExerciseDose(2, 8, 15), ledger.DoseFor(Sam, Goblet));
        Assert.Equal(new ExerciseDose(2, 8, 15), ledger.DoseFor(Sam, Row));
        Assert.Equal(new ExerciseDose(2, 8, 0), ledger.DoseFor(Sam, Wall));

        var after = ledger.Settle(Pages(2, Goblet, Wall, Row), All(Rated(1, "hard"), Rated(2, "right"), Rated(3, "right")));

        Assert.Equal(new ExerciseDose(2, 8, 10), after.DoseFor(Sam, Goblet));
        Assert.Equal(new ExerciseDose(2, 8, 15), after.DoseFor(Sam, Row));
        Assert.Equal(new ExerciseDose(2, 8, 0), after.DoseFor(Sam, Wall));
    }

    [Fact]
    public void HardOnABodyweightLiftStepsDownTheLadder()
    {
        var ledger = Climbed(Wall).Settle(Pages(2, Wall), Rated(1, "hard"));

        Assert.Equal(new ExerciseDose(2, 6, 0), ledger.DoseFor(Sam, Wall));
    }

    [Fact]
    public void ACircledWordAloneStillEases()
    {
        // p.5: a circled word on a page with no tick says the exercise was attempted, and the rating counts.
        var ledger = Climbed(Goblet).Settle(Pages(2, Goblet), CircledAlone(1, "hard"));

        Assert.Equal(new ExerciseDose(2, 8, 10), ledger.DoseFor(Sam, Goblet));
    }

    [Fact]
    public void HardNeverGoesBelowTheStartingStep()
    {
        var ledger = ProgressionLedger.Empty;
        for (var session = 0; session < 3; session++)
        {
            ledger = ledger.Settle(Pages(session, Goblet, Wall), All(Rated(1, "hard"), Rated(2, "hard")));
        }

        Assert.Equal(new ExerciseDose(2, 8, 5), ledger.DoseFor(Sam, Goblet));
        Assert.Equal(new ExerciseDose(2, 6, 0), ledger.DoseFor(Sam, Wall));
    }

    [Fact]
    public void HardBreaksARunOfEasies()
    {
        var ledger = ProgressionLedger.Empty
            .Settle(Pages(0, Goblet), Rated(1, "easy"))
            .Settle(Pages(1, Goblet), Rated(1, "hard"))
            .Settle(Pages(2, Goblet), Rated(1, "easy"));

        Assert.Equal(new ExerciseDose(2, 8, 5), ledger.DoseFor(Sam, Goblet));
    }

    // ---- StoppedEarlyEasesTheSession ----

    [Fact]
    public void StoppedEarlyEasesTheSession()
    {
        var ledger = Climbed(Goblet, Wall, Row, Hinge, Bridge);

        var after = ledger.Settle(
            Pages(2, Goblet, Wall, Row, Hinge, Bridge),
            All(Rated(1, "right"), Rated(2, "right"), Rated(3, "right"), Empty(4), Empty(5)));

        Assert.Equal(3, after.ExerciseCap(Sam));
        Assert.Null(ledger.ExerciseCap(Sam));
    }

    [Fact]
    public void StoppedEarlyEasesTheLastTickedExercise()
    {
        var ledger = Climbed(Goblet, Wall, Row, Hinge, Bridge);

        var after = ledger.Settle(
            Pages(2, Goblet, Wall, Row, Hinge, Bridge),
            All(Rated(1, "right"), Rated(2, "right"), Rated(3, "right"), Empty(4), Empty(5)));

        Assert.Equal(new ExerciseDose(2, 8, 10), after.DoseFor(Sam, Row));
        Assert.Equal(ledger.DoseFor(Sam, Goblet), after.DoseFor(Sam, Goblet));
        Assert.Equal(ledger.DoseFor(Sam, Wall), after.DoseFor(Sam, Wall));
        Assert.Equal(ledger.DoseFor(Sam, Hinge), after.DoseFor(Sam, Hinge));
    }

    [Fact]
    public void AHardLastPageIsNotEasedTwice()
    {
        var ledger = Climbed(Goblet, Row, Wall);

        var after = ledger.Settle(Pages(2, Goblet, Row, Wall), All(Rated(1, "right"), Rated(2, "hard"), Empty(3)));

        Assert.Equal(new ExerciseDose(2, 8, 10), after.DoseFor(Sam, Row));
        Assert.Equal(2, after.ExerciseCap(Sam));
    }

    [Fact]
    public void TheCapNeverLeavesLessThanTwoExercises()
    {
        var after = ProgressionLedger.Empty.Settle(Pages(0, Goblet, Wall, Row), All(Rated(1, "right"), Empty(2), Empty(3)));

        Assert.Equal(2, after.ExerciseCap(Sam));
    }

    [Fact]
    public void ASkippedPageInTheMiddleIsNotStoppingEarly()
    {
        var after = ProgressionLedger.Empty.Settle(Pages(0, Goblet, Wall, Row), All(Rated(1, "easy"), Empty(2), Rated(3, "easy")));

        Assert.Equal(new ExerciseDose(2, 8, 10), after.DoseFor(Sam, Goblet));
        Assert.Null(after.ExerciseCap(Sam));
        Assert.Equal(new ExerciseDose(2, 6, 0), after.DoseFor(Sam, Wall));
    }

    [Fact]
    public void APageNotReadIsNotStoppingEarly()
    {
        var after = ProgressionLedger.Empty.Settle(Pages(0, Goblet, Wall, Row), All(Rated(1, "right"), Rated(2, "right"), Unread(3)));

        Assert.Null(after.ExerciseCap(Sam));
        Assert.Equal(new ExerciseDose(2, 8, 10), after.DoseFor(Sam, Goblet));
    }

    [Fact]
    public void AFullSessionLiftsTheCapOneStep()
    {
        var capped = ProgressionLedger.Empty.Settle(Pages(0, Goblet, Wall, Row, Hinge), All(Rated(1, "right"), Rated(2, "right"), Empty(3), Empty(4)));
        Assert.Equal(2, capped.ExerciseCap(Sam));

        var fullAtTheCap = capped.Settle(Pages(1, Goblet, Wall), All(Rated(1, "right"), Rated(2, "right")));
        Assert.Equal(3, fullAtTheCap.ExerciseCap(Sam));

        var fullAgain = fullAtTheCap.Settle(Pages(2, Goblet, Wall, Row), All(Rated(1, "right"), Rated(2, "right"), Rated(3, "right")));
        Assert.Equal(4, fullAgain.ExerciseCap(Sam));
    }

    [Fact]
    public void ASessionWithAGapInItDoesNotLiftTheCap()
    {
        var capped = ProgressionLedger.Empty.Settle(Pages(0, Goblet, Wall, Row, Hinge), All(Rated(1, "right"), Rated(2, "right"), Rated(3, "right"), Empty(4)));
        Assert.Equal(3, capped.ExerciseCap(Sam));

        var withAGap = capped.Settle(Pages(1, Goblet, Wall, Row), All(Rated(1, "right"), Empty(2), Rated(3, "right")));

        Assert.Equal(3, withAGap.ExerciseCap(Sam));
    }

    [Fact]
    public void TheCapGoesWhenTheFullSessionIsTheWholeSession()
    {
        var capped = ProgressionLedger.Empty.Settle(Pages(0, Goblet, Wall, Row, Hinge, Bridge), All(Rated(1, "right"), Rated(2, "right"), Rated(3, "right"), Rated(4, "right"), Empty(5)));
        Assert.Equal(4, capped.ExerciseCap(Sam));

        var allFive = capped.Settle(Pages(1, Goblet, Wall, Row, Hinge, Bridge), All(Rated(1, "right"), Rated(2, "right"), Rated(3, "right"), Rated(4, "right"), Rated(5, "right")));

        Assert.Null(allFive.ExerciseCap(Sam));
    }

    // ---- EasyProgressesTheLoad ----

    [Fact]
    public void EasyProgressesTheLoad()
    {
        var once = ProgressionLedger.Empty.Settle(Pages(0, Goblet, Wall), All(Rated(1, "easy"), Rated(2, "easy")));
        Assert.Equal(new ExerciseDose(2, 8, 10), once.DoseFor(Sam, Goblet));
        Assert.Equal(new ExerciseDose(2, 6, 0), once.DoseFor(Sam, Wall));

        var twice = once.Settle(Pages(1, Goblet, Wall), All(Rated(1, "easy"), Rated(2, "easy")));

        Assert.Equal(new ExerciseDose(2, 8, 15), twice.DoseFor(Sam, Goblet));
        Assert.Equal(new ExerciseDose(2, 8, 0), twice.DoseFor(Sam, Wall));
    }

    [Fact]
    public void ARightBreaksTheRunOfEasies()
    {
        var ledger = ProgressionLedger.Empty
            .Settle(Pages(0, Goblet), Rated(1, "easy"))
            .Settle(Pages(1, Goblet), Rated(1, "right"))
            .Settle(Pages(2, Goblet), Rated(1, "easy"));

        Assert.Equal(new ExerciseDose(2, 8, 10), ledger.DoseFor(Sam, Goblet));
    }

    [Fact]
    public void ATickWithNoCircleIsDoneAsWritten()
    {
        var ledger = ProgressionLedger.Empty
            .Settle(Pages(0, Goblet), Rated(1, "easy"))
            .Settle(Pages(1, Goblet), TickedOnly(1));

        Assert.Equal(new ExerciseDose(2, 8, 10), ledger.DoseFor(Sam, Goblet));

        // ...and says nothing either way about the run: the next easy is the second.
        var next = ledger.Settle(Pages(2, Goblet), Rated(1, "easy"));
        Assert.Equal(new ExerciseDose(2, 8, 15), next.DoseFor(Sam, Goblet));
    }

    [Fact]
    public void ASkippedPageChangesNothing()
    {
        var ledger = Climbed(Goblet, Wall);

        var after = ledger.Settle(Pages(2, Goblet, Wall), All(Rated(1, "right"), Empty(2)));

        Assert.Equal(new ExerciseDose(2, 8, 0), ledger.DoseFor(Sam, Wall));
        Assert.Equal(ledger.DoseFor(Sam, Wall), after.DoseFor(Sam, Wall));
    }

    [Fact]
    public void APageNotReadChangesNothing()
    {
        var ledger = Climbed(Goblet, Wall);

        var after = ledger.Settle(Pages(2, Goblet, Wall), All(Unread(1), Unread(2)));

        Assert.Equal(new ExerciseDose(2, 8, 15), ledger.DoseFor(Sam, Goblet));
        Assert.Equal(ledger.DoseFor(Sam, Goblet), after.DoseFor(Sam, Goblet));
        Assert.Equal(ledger.DoseFor(Sam, Wall), after.DoseFor(Sam, Wall));
        Assert.Null(after.ExerciseCap(Sam));
    }

    [Fact]
    public void AnUnknownWordIsNotARating()
    {
        var ledger = ProgressionLedger.Empty
            .Settle(Pages(0, Goblet), Rated(1, "easy"))
            .Settle(Pages(1, Goblet), Rated(1, "medium"));

        Assert.Equal(new ExerciseDose(2, 8, 10), ledger.DoseFor(Sam, Goblet));
    }

    [Fact]
    public void TheTopRungHolds()
    {
        var ledger = ProgressionLedger.Empty;
        for (var session = 0; session < 20; session++)
        {
            ledger = ledger.Settle(Pages(session, Wall), Rated(1, "easy"));
        }

        Assert.Equal(new ExerciseDose(3, 12, 0), ledger.DoseFor(Sam, Wall));
    }

    [Fact]
    public void TheBodyweightLadderOnlyEverRises()
    {
        var ledger = ProgressionLedger.Empty;
        var last = ledger.DoseFor(Sam, Wall).TotalReps;
        for (var session = 0; session < 14; session++)
        {
            ledger = ledger.Settle(Pages(session, Wall), Rated(1, "easy"));
            var now = ledger.DoseFor(Sam, Wall).TotalReps;
            Assert.True(now >= last, $"The ladder went down after an easy session ({last} to {now}).");
            last = now;
        }

        Assert.Equal(36, last);
    }

    // ---- who and what counts ----

    [Fact]
    public void EachPersonKeepsTheirOwnLoad()
    {
        var ledger = Climbed(Goblet);

        Assert.Equal(new ExerciseDose(2, 8, 15), ledger.DoseFor(Sam, Goblet));
        Assert.Equal(new ExerciseDose(2, 8, 10), ledger.DoseFor(Lee, Goblet));
        Assert.Equal(new ExerciseDose(2, 8, 15), ledger.DoseFor(" sam ", Goblet));
    }

    [Fact]
    public void OnlyStrengthSessionsMoveTheLoad()
    {
        var mobility = ProgressionLedger.Empty
            .Settle(Pages(Sam, SessionKind.Mobility, 0, Hinge), Rated(1, "easy"))
            .Settle(Pages(Sam, SessionKind.Mobility, 1, Hinge), Rated(1, "easy"));
        var walk = ProgressionLedger.Empty.Settle(Pages(Sam, SessionKind.OutdoorCardio, 0, new Exercise("Brisk walk")), Empty(1));

        Assert.Equal(new ExerciseDose(2, 6, 0), mobility.DoseFor(Sam, Hinge));
        Assert.Null(walk.ExerciseCap(Sam));
    }

    [Fact]
    public void AnExerciseNeverSeenStartsAtTheStart()
    {
        var empty = ProgressionLedger.Empty;

        Assert.Equal(new ExerciseDose(2, 8, 10), empty.DoseFor(Sam, Goblet));
        Assert.Equal(new ExerciseDose(2, 6, 0), empty.DoseFor(Sam, Wall));
        Assert.Equal(new ExerciseDose(2, 6, 0), empty.DoseFor(Sam, Bridge));
        Assert.Null(empty.ExerciseCap(Sam));
    }

    [Fact]
    public void TheAssessmentSetsWhereABodyweightLiftStarts()
    {
        var sitToStand = new Exercise("Sit to stand from a chair");
        AssessmentBaselines Read(string push, string stands) => AssessmentBaselines.Read(
            [.. AssessmentCatalogue.MeasuresOf(AssessmentTest.StrengthBaselines)],
            [new InkResolution.Written(AssessmentCatalogue.WallPushUpsId, push), new InkResolution.Written(AssessmentCatalogue.SitToStandId, stands)]);

        var weak = Read("6", "8");
        var middling = Read("15", "15");
        var strong = Read("30", "30");

        Assert.Equal(new ExerciseDose(2, 6, 0), ProgressionLedger.Empty.DoseFor(Sam, sitToStand, weak));
        Assert.Equal(new ExerciseDose(2, 8, 0), ProgressionLedger.Empty.DoseFor(Sam, Wall, middling));
        Assert.Equal(new ExerciseDose(2, 10, 0), ProgressionLedger.Empty.DoseFor(Sam, Wall, strong));
        Assert.Equal(new ExerciseDose(2, 10, 0), ProgressionLedger.Empty.DoseFor(Sam, sitToStand, strong));
        Assert.Equal(new ExerciseDose(2, 6, 0), ProgressionLedger.Empty.DoseFor(Sam, Hinge, strong));
        Assert.Equal(new ExerciseDose(2, 8, 10), ProgressionLedger.Empty.DoseFor(Sam, Goblet, strong));
    }

    [Fact]
    public void ABaselineNeverOverridesWhatThePagesSaid()
    {
        var strong = AssessmentBaselines.Read(
            [.. AssessmentCatalogue.MeasuresOf(AssessmentTest.StrengthBaselines)],
            [new InkResolution.Written(AssessmentCatalogue.WallPushUpsId, "30")]);
        var eased = ProgressionLedger.Empty
            .Settle(Pages(0, Wall), Rated(1, "hard"), strong);

        Assert.Equal(new ExerciseDose(2, 8, 0), eased.DoseFor(Sam, Wall, strong));
    }

    // ---- ProgressionReachesTheEncouragementFacts ----

    [Fact]
    public void ProgressionReachesTheEncouragementFacts()
    {
        var up = Climbed(Goblet);
        var plan = new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [Goblet, Wall]);

        var prescriptions = ProgressionPlan.Prescribe(up, Sam, plan);

        var goblet = prescriptions[0];
        Assert.Equal(Goblet, goblet.Exercise);
        Assert.Equal(new ExerciseDose(2, 8, 15), goblet.Dose);
        Assert.NotNull(goblet.News);
        Assert.Equal(new ProgressionFacts(Goblet.Name, 15, 10, "lb"), goblet.News);

        var seat = SeatFacts.WorkoutEncouragement(goblet.News!);
        Assert.NotNull(seat);
        Assert.Equal("15 lb today, up from 10.", seat!.Fallback);
        Assert.Null(prescriptions[1].News);
    }

    [Fact]
    public void ABodyweightRiseIsNewsInReps()
    {
        var up = Climbed(Wall);

        var prescription = ProgressionPlan.Prescribe(up, Sam, new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [Wall])).Single();

        Assert.Equal(new ProgressionFacts(Wall.Name, 16, 12, "reps"), prescription.News);
        Assert.Equal("16 reps today, up from 12.", SeatFacts.WorkoutEncouragement(prescription.News!)!.Fallback);
    }

    [Fact]
    public void NothingRoseSoThereIsNoNews()
    {
        var held = Climbed(Goblet).Settle(Pages(2, Goblet), Rated(1, "right"));
        var eased = Climbed(Goblet).Settle(Pages(2, Goblet), Rated(1, "hard"));
        var session = new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [Goblet]);

        Assert.Null(ProgressionPlan.Prescribe(held, Sam, session).Single().News);
        Assert.Null(ProgressionPlan.Prescribe(eased, Sam, session).Single().News);
        Assert.Null(ProgressionPlan.Prescribe(ProgressionLedger.Empty, Sam, session).Single().News);
    }

    [Fact]
    public void TheNewsLastsOneSessionOnly()
    {
        var up = Climbed(Goblet);
        var session = new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [Goblet]);
        Assert.NotNull(ProgressionPlan.Prescribe(up, Sam, session).Single().News);

        var afterTheNextSession = up.Settle(Pages(2, Goblet), TickedOnly(1));

        Assert.Null(ProgressionPlan.Prescribe(afterTheNextSession, Sam, session).Single().News);
    }

    [Fact]
    public void ASessionNotDoneKeepsItsNews()
    {
        var up = Climbed(Goblet);
        var session = new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [Goblet]);

        var skipped = up.Settle(Pages(2, Goblet), Empty(1));

        Assert.NotNull(ProgressionPlan.Prescribe(skipped, Sam, session).Single().News);
    }

    // ---- the plan ----

    [Fact]
    public void TheCapTrimsAStrengthSession()
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(1, SessionKind.Strength, [Sam])]);
        var capped = ProgressionLedger.Empty.Settle(Pages(0, Goblet, Wall, Row, Hinge), All(Rated(1, "right"), Rated(2, "right"), Rated(3, "right"), Empty(4)));
        Assert.Equal(3, capped.ExerciseCap(Sam));

        var eased = ProgressionPlan.ApplyCaps(plan, capped);

        var before = HandmadePlans.OnDay(plan, Sam, 1).Session;
        var after = HandmadePlans.OnDay(eased, Sam, 1).Session;
        Assert.Equal(before.Exercises.Take(3).Select(item => item.Name), after.Exercises.Select(item => item.Name));
        Assert.True(after.Duration < before.Duration);
        Assert.True(after.Duration >= TimeSpan.FromMinutes(20));
        Assert.Equal(0, after.Duration.TotalMinutes % 5);
        Assert.Equal(SessionKind.Strength, after.Kind);
        Assert.Equal(HandmadePlans.OnDay(plan, Sam, 1).Start, HandmadePlans.OnDay(eased, Sam, 1).Start);
    }

    [Fact]
    public void ACapLeavesOtherSessionsAndPeopleAlone()
    {
        var plan = HandmadePlans.Week(
            [HandmadePlans.Placed(1, SessionKind.Strength, [Sam]), HandmadePlans.Placed(2, SessionKind.Mobility, [Sam]), HandmadePlans.Placed(3, SessionKind.OutdoorCardio, [Sam])],
            [HandmadePlans.Placed(1, SessionKind.Strength, [Lee])]);
        var capped = ProgressionLedger.Empty.Settle(Pages(0, Goblet, Wall, Row), All(Rated(1, "right"), Empty(2), Empty(3)));

        var eased = ProgressionPlan.ApplyCaps(plan, capped);

        Assert.Equal(HandmadePlans.OnDay(plan, Sam, 2), HandmadePlans.OnDay(eased, Sam, 2));
        Assert.Equal(HandmadePlans.OnDay(plan, Sam, 3), HandmadePlans.OnDay(eased, Sam, 3));
        Assert.Equal(5, HandmadePlans.OnDay(plan, Lee, 1).Session.Exercises.Count);
        Assert.Equal(5, HandmadePlans.OnDay(eased, Lee, 1).Session.Exercises.Count);
        Assert.Equal(2, HandmadePlans.OnDay(eased, Sam, 1).Session.Exercises.Count);
    }

    [Fact]
    public void ASharedSessionTakesTheLowerCapForBoth()
    {
        var joint = HandmadePlans.Placed(2, SessionKind.Strength, [Sam, Lee], Together.AfterBedtime, hour: 20);
        var plan = HandmadePlans.Week([], [], [joint]);
        var capped = ProgressionLedger.Empty.Settle(Pages(0, Goblet, Wall, Row), All(Rated(1, "right"), Rated(2, "right"), Empty(3)));
        Assert.Equal(2, capped.ExerciseCap(Sam));

        var eased = ProgressionPlan.ApplyCaps(plan, capped);

        Assert.Equal(2, Assert.Single(eased.JointSessions).Session.Exercises.Count);
        Assert.Same(eased.JointSessions[0], HandmadePlans.OnDay(eased, Sam, 2));
        Assert.Same(eased.JointSessions[0], HandmadePlans.OnDay(eased, Lee, 2));
    }

    [Fact]
    public void NoHistoryLeavesThePlanExactlyAsPlanned()
    {
        var plan = Plan(Calendars());

        var same = ProgressionPlan.ApplyCaps(plan, ProgressionLedger.Empty);

        Assert.Same(plan, same);
    }

    [Fact]
    public void ACapNeverAddsExercisesToAShortSession()
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(1, SessionKind.Strength, [Sam], exercises: [Goblet, Wall])]);
        var capped = ProgressionLedger.Empty.Settle(Pages(0, Goblet, Wall, Row, Hinge), All(Rated(1, "right"), Rated(2, "right"), Rated(3, "right"), Empty(4)));

        var eased = ProgressionPlan.ApplyCaps(plan, capped);

        Assert.Equal(3, capped.ExerciseCap(Sam));
        Assert.Equal(2, HandmadePlans.OnDay(eased, Sam, 1).Session.Exercises.Count);
        Assert.Equal(HandmadePlans.OnDay(plan, Sam, 1).Session.Duration, HandmadePlans.OnDay(eased, Sam, 1).Session.Duration);
    }

    [Fact]
    public void AnEasedSessionStillNeedsOnlyTickedEquipment()
    {
        var plan = Plan(Calendars(), EquipmentSet.Of([EquipmentCatalogue.MatId]));
        var capped = ProgressionLedger.Empty.Settle(Pages(0, Goblet, Wall, Row), All(Rated(1, "right"), Empty(2), Empty(3)));

        var eased = ProgressionPlan.ApplyCaps(plan, capped);

        foreach (var session in eased.Plans.SelectMany(week => week.Sessions))
        {
            Assert.All(session.Session.EquipmentNeeded, id => Assert.Equal(EquipmentCatalogue.MatId, id));
        }
    }

    // ---- the stored file ----

    [Fact]
    public void TheLedgerRoundTripsThroughItsFile()
    {
        var ledger = Climbed(Goblet, Wall)
            .Settle(Pages(Lee, SessionKind.Strength, 2, Row), Rated(1, "easy"))
            .Settle(Pages(3, Goblet, Wall, Row), All(Rated(1, "right"), Empty(2), Empty(3)));

        var read = ProgressionLedger.FromJson(ledger.ToJson());

        Assert.False(read.WholeFileUnreadable);
        Assert.Equal(0, read.UnreadableEntries);
        foreach (var exercise in new[] { Goblet, Wall, Row })
        {
            Assert.Equal(ledger.DoseFor(Sam, exercise), read.Ledger.DoseFor(Sam, exercise));
            Assert.Equal(ledger.DoseFor(Lee, exercise), read.Ledger.DoseFor(Lee, exercise));
        }

        Assert.Equal(ledger.ExerciseCap(Sam), read.Ledger.ExerciseCap(Sam));
        Assert.Equal(ProgressionPlan.Prescribe(ledger, Sam, new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [Goblet])).Single().News,
            ProgressionPlan.Prescribe(read.Ledger, Sam, new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [Goblet])).Single().News);
    }

    [Fact]
    public void TheStoredNamesAreLiteralsAndPinned()
    {
        var ledger = Climbed(Goblet).Settle(Pages(2, Goblet, Wall, Row), All(Rated(1, "right"), Empty(2), Empty(3)));

        using var document = JsonDocument.Parse(ledger.ToJson());
        var root = document.RootElement;

        Assert.Equal(["caps", "exercises", "settled", "version"], root.EnumerateObject().Select(item => item.Name).Order().ToArray());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        var exercise = root.GetProperty("exercises").EnumerateArray().First();
        Assert.Equal(["easyRun", "exercise", "person", "pounds", "rung", "upFrom"], exercise.EnumerateObject().Select(item => item.Name).Order().ToArray());
        var cap = Assert.Single(root.GetProperty("caps").EnumerateArray());
        Assert.Equal(["cap", "person"], cap.EnumerateObject().Select(item => item.Name).Order().ToArray());
    }

    [Fact]
    public void NothingStoredIsAnEmptyLedgerWithNoProblem()
    {
        foreach (var stored in new string?[] { null, "", "  " })
        {
            var read = ProgressionLedger.FromJson(stored);
            Assert.False(read.WholeFileUnreadable);
            Assert.Equal(0, read.UnreadableEntries);
            Assert.Null(read.Ledger.ExerciseCap(Sam));
        }
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{\"version\":2,\"exercises\":[],\"caps\":[]}")]
    [InlineData("{\"version\":1}")]
    public void AFileThatIsNotALedgerIsFlaggedNotRead(string stored)
    {
        var read = ProgressionLedger.FromJson(stored);

        Assert.True(read.WholeFileUnreadable);
        Assert.Equal(new ExerciseDose(2, 6, 0), read.Ledger.DoseFor(Sam, Wall));
    }

    [Fact]
    public void AnEntryThatCannotBeReadIsCountedAndTheRestKept()
    {
        var good = Climbed(Goblet).ToJson();
        var broken = good.Replace("\"exercises\":[", "\"exercises\":[{\"person\":\"Sam\",\"exercise\":\"Nonsense\",\"rung\":\"high\",\"pounds\":0,\"easyRun\":0},{\"person\":\"\",\"exercise\":\"x\",\"rung\":0,\"pounds\":0,\"easyRun\":0},", StringComparison.Ordinal);

        var read = ProgressionLedger.FromJson(broken);

        Assert.False(read.WholeFileUnreadable);
        Assert.Equal(2, read.UnreadableEntries);
        Assert.Equal(new ExerciseDose(2, 8, 15), read.Ledger.DoseFor(Sam, Goblet));
    }

    [Fact]
    public void AnOutOfRangeRungIsAnUnreadableEntry()
    {
        var json = "{\"version\":1,\"exercises\":[{\"person\":\"Sam\",\"exercise\":\"Push-ups against a wall\",\"rung\":99,\"pounds\":0,\"easyRun\":0}],\"caps\":[]}";

        var read = ProgressionLedger.FromJson(json);

        Assert.Equal(1, read.UnreadableEntries);
        Assert.Equal(new ExerciseDose(2, 6, 0), read.Ledger.DoseFor(Sam, Wall));
    }

    // ---- refusals ----

    [Fact]
    public void ASessionWithNoPagesOrNoOneIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ProgressionLedger.Empty.Settle(new SessionPages(Sam, Day(0), SessionKind.Strength, []), Array.Empty<PageResolutions>()));
        Assert.Throws<ArgumentException>(() => ProgressionLedger.Empty.Settle(new SessionPages(" ", Day(0), SessionKind.Strength, Pages(0, Goblet).Pages), Array.Empty<PageResolutions>()));
    }

    [Fact]
    public void ThePageReaderSeesTickCircleAndSilence()
    {
        var reads = PageReading.Read(
            Pages(0, Goblet, Wall, Row, Hinge, Bridge),
            All(Rated(1, "hard"), CircledAlone(2, "easy"), Empty(3), Unread(4), TickedOnly(5)));

        Assert.Equal([PageVerdict.Done, PageVerdict.Done, PageVerdict.NotDone, PageVerdict.Unknown, PageVerdict.Done], reads.Select(item => item.Verdict).ToArray());
        Assert.Equal<ExerciseRating?>([ExerciseRating.Hard, ExerciseRating.Easy, null, null, null], reads.Select(item => item.Rating).ToArray());
    }
}
