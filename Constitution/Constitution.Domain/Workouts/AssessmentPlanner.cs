namespace Constitution.Domain.Workouts;

/// <summary>An assessment session as placed, with the test it is and what is read back from it.</summary>
public sealed record AssessmentSession(PlannedSession Placed, AssessmentTest Test, IReadOnlyList<AssessmentMeasure> Measures);

/// <summary>The assessment week for everyone asked about: the plan, and the same sessions as tests.</summary>
public sealed record AssessmentWeek(HouseholdWorkoutPlan Plan, IReadOnlyList<AssessmentSession> Sessions);

/// <summary>
/// The first week (C-5.1.2): three gentle sessions each, a strength baseline, a timed walk (or jog) and a mobility
/// check, placed by the ordinary planner's own rules so that every promise it makes still holds (gaps of 30 minutes,
/// waking hours, no two hard days together, nothing on a Sunday).
///
/// <para>It asks the planner for each person alone, with their history cleared to "ask for three", then turns what
/// came back into the three tests. Planned alone means no joint session: a baseline is one person's own number. The
/// Sunday walk the ordinary week adds is dropped, because the assessment is three sessions and not four. A week that
/// cannot hold all three (a crowded calendar) holds the ones that fit, and an unreadable calendar plans nothing and
/// says why, exactly as the ordinary planner does.</para>
/// </summary>
public static class AssessmentPlanner
{
    public static AssessmentWeek Plan(WorkoutPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.People is null || request.People.Count == 0)
        {
            throw new ArgumentException("There is no one to plan for.", nameof(request));
        }

        var plans = new List<WeeklyPlan>();
        var sessions = new List<AssessmentSession>();
        foreach (var person in request.People)
        {
            // Done and skipped last week are the ordinary week's business; only the last hard day still protects the body.
            var alone = request with
            {
                People = [person with { Recovery = new RecoveryInputs { LastHardSessionDay = person.Recovery.LastHardSessionDay } }],
            };
            var week = WorkoutPlanner.Plan(alone).Plans.Single();

            var tests = week.Sessions
                .Where(item => item.Session.Kind != SessionKind.GentleWalk)
                .Select(item => Convert(item))
                .ToList();
            sessions.AddRange(tests);
            plans.Add(week with { Sessions = tests.Select(item => item.Placed).ToList() });
        }

        return new AssessmentWeek(new HouseholdWorkoutPlan(request.WeekStart, plans, []), sessions);
    }

    private static AssessmentSession Convert(PlannedSession planned)
    {
        var test = AssessmentCatalogue.TestFor(planned.Session.Kind);
        var session = AssessmentLibrary.Build(test, planned.Session.Kind, planned.Session.Duration);
        return new AssessmentSession(planned with { Session = session }, test, AssessmentCatalogue.MeasuresOf(test));
    }
}

/// <summary>The assessment sessions themselves: bodyweight, short, and named for what they measure.</summary>
internal static class AssessmentLibrary
{
    private const double StrengthMinutes = 30;
    private const double WalkMinutes = 25;
    private const double MobilityMinutes = 20;

    /// <summary>A session for <paramref name="test"/>, no longer than the ordinary session it replaces (which already fits its gap).</summary>
    public static Session Build(AssessmentTest test, SessionKind plannedKind, TimeSpan plannedDuration)
    {
        var duration = TimeSpan.FromMinutes(Math.Min(plannedDuration.TotalMinutes, Preferred(test)));
        return test switch
        {
            AssessmentTest.StrengthBaselines => new Session(
                SessionKind.Strength,
                duration,
                false,
                [new("Sit to stand from a chair, as many as you can in 30 seconds"), new("Push-ups against a wall, as many as you can in 30 seconds")]),
            AssessmentTest.TimedWalk when plannedKind == SessionKind.Treadmill => new Session(
                SessionKind.Treadmill,
                duration,
                true,
                [
                    new("Warm-up walk on the treadmill", Equipment.EquipmentCatalogue.TreadmillId),
                    new("Walk or jog for 10 minutes and note the distance", Equipment.EquipmentCatalogue.TreadmillId),
                    new("Cool-down walk on the treadmill", Equipment.EquipmentCatalogue.TreadmillId),
                ]),
            AssessmentTest.TimedWalk => new Session(
                SessionKind.OutdoorCardio,
                duration,
                true,
                [new("Warm-up walk"), new("Walk or jog for 10 minutes and note the distance"), new("Cool-down walk")]),
            AssessmentTest.MobilityCheck => new Session(
                SessionKind.Mobility,
                duration,
                false,
                [new("Seated toe reach"), new("Neck and shoulder rolls")]),
            _ => throw new ArgumentOutOfRangeException(nameof(test), test, "There is no such assessment test."),
        };
    }

    private static double Preferred(AssessmentTest test) => test switch
    {
        AssessmentTest.StrengthBaselines => StrengthMinutes,
        AssessmentTest.TimedWalk => WalkMinutes,
        AssessmentTest.MobilityCheck => MobilityMinutes,
        _ => throw new ArgumentOutOfRangeException(nameof(test), test, "There is no such assessment test."),
    };
}
