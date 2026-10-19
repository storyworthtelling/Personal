using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;

namespace Constitution.Domain.Workouts;

/// <summary>What was done about a session the conditions made a bad idea outdoors.</summary>
public enum ConditionAction
{
    /// <summary>Same session, another start inside the same gap.</summary>
    MovedInTime,

    /// <summary>Replaced by an indoor session in the same slot.</summary>
    MovedIndoors,

    /// <summary>Taken off the day; nothing replaces it.</summary>
    Rested,
}

/// <summary>
/// One session the conditions changed. <see cref="Original"/> is what the plan held before and <see cref="Replacement"/> what
/// it holds now (null for a rest day). <see cref="Line"/> is the one-line reason for the card and the Workouts tab, from a
/// fixed vocabulary.
///
/// <para>Whether the workout document has already gone out, and whether it is re-sent, is C-2.3.6's decision, not this
/// one's: the original's start is here so that step can compare it with the clock.</para>
/// </summary>
public sealed record ConditionChange(ConditionAction Action, ConditionCause Cause, PlannedSession Original, PlannedSession? Replacement, string Line)
{
    public DateOnly Day => Original.Day;

    /// <summary>Everyone the session belonged to; more than one for a joint session.</summary>
    public IReadOnlyList<string> People => Original.Participants;
}

/// <summary>The week after the conditions have had their say, and what they changed. <see cref="ConditionsWereRead"/> is false when the forecast could not be read, and then nothing was changed.</summary>
public sealed record ConditionsAdaptation(HouseholdWorkoutPlan Plan, IReadOnlyList<ConditionChange> Changes, bool ConditionsWereRead);

/// <summary>The words of the reason lines. Nothing from a forecast, a place or a calendar ever reaches a line.</summary>
public static class ConditionsWords
{
    /// <summary>What a person's plan says when the conditions took away every session it had.</summary>
    public const string WeekCalledOff = "The weather called off every session this week.";

    /// <summary>
    /// The line for one change: "Moved indoors: rain forecast", "Rest day: poor air", "Moved later: heat forecast".
    /// <paramref name="later"/> says which way a <see cref="ConditionAction.MovedInTime"/> went and is read for nothing else.
    /// </summary>
    public static string For(ConditionAction action, ConditionCause cause, bool later = true)
    {
        var lead = action switch
        {
            ConditionAction.MovedIndoors => "Moved indoors",
            ConditionAction.Rested => "Rest day",
            ConditionAction.MovedInTime => later ? "Moved later" : "Moved earlier",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "There is no such action."),
        };
        return $"{lead}: {Phrase(cause)}";
    }

    private static string Phrase(ConditionCause cause) => cause switch
    {
        ConditionCause.Rain => "rain forecast",
        ConditionCause.Snow => "snow forecast",
        ConditionCause.Ice => "ice forecast",
        ConditionCause.Heat => "heat forecast",
        ConditionCause.PoorAir => "poor air",
        ConditionCause.Darkness => "after dark",
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "There is no such cause."),
    };
}

/// <summary>
/// Adapts a planned week to the forecast (C-5.2.2): an outdoor session whose slot has a bad hour (rain, snow, ice, heat,
/// poor air, and darkness if asked for) is first tried at another time inside the gap it was found in, then replaced by an
/// indoor session, or taken off the day.
///
/// <para><b>What counts as outdoors.</b> An outdoor cardio session, and a gentle walk that does not need a treadmill. An
/// indoor replacement is therefore indoors for the next pass too: applying this twice to the same week changes nothing the
/// second time.</para>
///
/// <para><b>The replacements.</b> Outdoor cardio becomes a treadmill session if the treadmill is ticked, else mobility (not hard,
/// so it cannot put two hard days together). A solo gentle walk becomes a treadmill walk if the treadmill is ticked, else it is a
/// rest day, because Sunday holds a gentle walk and nothing else. A joint walk is a rest day for everyone: a family walk is not
/// done on a treadmill. The replacement keeps the slot (day, start and gap) and the length, so it fits wherever the original
/// did. Mobility is the only replacement shorter than the session it replaces.</para>
///
/// <para><b>What it leaves alone.</b> A session that has started or is past (before the request's clock); a day anyone in the
/// session is away (the forecast is for home); an indoor session; hours the forecast does not hold. A forecast that could not be
/// read changes nothing: it is not a reason, and <see cref="ConditionsAdaptation.ConditionsWereRead"/> says so. Whether a
/// document already delivered is re-sent is C-2.3.6's decision.</para>
///
/// <para><b>The one thing it does not control:</b> placement (C-5.3.2) takes the planner's start if it fits and otherwise the
/// earliest minute that does. A moved session keeps room to shower inside its gap so the start stands; if the calendar changes
/// between this and placement, placement decides and does not look at the weather.</para>
/// </summary>
public static class ConditionsAdapter
{
    public static ConditionsAdaptation Apply(
        WorkoutPlanRequest request,
        HouseholdWorkoutPlan plan,
        ConditionsRead conditions,
        ConditionsLimits? limits = null,
        Func<string, DateOnly, bool>? isAway = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(conditions);
        var applied = limits ?? new ConditionsLimits();
        applied.Check();
        if (!conditions.IsAvailable)
        {
            return new ConditionsAdaptation(plan, [], false);
        }

        var outcomes = new Dictionary<PlannedSession, PlannedSession?>(ReferenceEqualityComparer.Instance);
        var changes = new List<ConditionChange>();
        foreach (var session in WeekRewrite.Distinct(plan).OrderBy(item => item.Start))
        {
            if (Decide(request, conditions, applied, isAway, session) is { } change)
            {
                outcomes[session] = change.Replacement;
                changes.Add(change);
            }
        }

        return changes.Count == 0
            ? new ConditionsAdaptation(plan, [], true)
            : new ConditionsAdaptation(WeekRewrite.Apply(plan, outcomes, (week, kept) => kept.Count == 0 ? ConditionsWords.WeekCalledOff : week.NothingBecause), changes, true);
    }

    /// <summary>Asks the source for the whole local week, then <see cref="Apply"/>.</summary>
    public static async Task<ConditionsAdaptation> AdaptAsync(
        WorkoutPlanRequest request,
        HouseholdWorkoutPlan plan,
        IConditionsSource source,
        CancellationToken cancellation,
        ConditionsLimits? limits = null,
        Func<string, DateOnly, bool>? isAway = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        var zone = request.Rules.Zone;
        var read = await source.ReadAsync(
            ZoneClock.StartOfDay(request.WeekStart, zone),
            ZoneClock.StartOfDay(request.WeekStart.AddDays(7), zone),
            cancellation);
        return Apply(request, plan, read, limits, isAway);
    }

    private static ConditionChange? Decide(
        WorkoutPlanRequest request,
        ConditionsRead conditions,
        ConditionsLimits limits,
        Func<string, DateOnly, bool>? isAway,
        PlannedSession session)
    {
        if (!IsOutdoors(session.Session) || session.Start < request.Now)
        {
            return null;
        }

        if (isAway is not null && session.Participants.Any(person => isAway(person, session.Day)))
        {
            return null;
        }

        if (ConditionsAssessment.Of(conditions, session.Start, session.End, limits) is not { } cause)
        {
            return null;
        }

        if (limits.MayMoveWithinGap && Move(request, conditions, limits, session) is { } moved)
        {
            return new ConditionChange(ConditionAction.MovedInTime, cause, session, moved, ConditionsWords.For(ConditionAction.MovedInTime, cause, moved.Start > session.Start));
        }

        var indoors = Indoors(request, session);
        return indoors is null
            ? new ConditionChange(ConditionAction.Rested, cause, session, null, ConditionsWords.For(ConditionAction.Rested, cause))
            : new ConditionChange(ConditionAction.MovedIndoors, cause, session, indoors, ConditionsWords.For(ConditionAction.MovedIndoors, cause));
    }

    private static bool IsOutdoors(Session session) =>
        session.Kind is SessionKind.OutdoorCardio or SessionKind.GentleWalk
        && !session.EquipmentNeeded.Contains(EquipmentCatalogue.TreadmillId);

    // The start nearest the planned one, on the step grid from the gap's start, whose whole slot is clear and which still
    // leaves room to shower inside the gap. Nothing earlier than now.
    private static PlannedSession? Move(WorkoutPlanRequest request, ConditionsRead conditions, ConditionsLimits limits, PlannedSession session)
    {
        var length = session.Session.Duration;
        var room = session.Session.IsSweaty ? limits.ShowerRoom : TimeSpan.Zero;
        PlannedSession? best = null;
        for (var start = session.Gap.Start; start + length + room <= session.Gap.End; start += limits.MoveStep)
        {
            if (start < request.Now || start == session.Start || ConditionsAssessment.Of(conditions, start, start + length, limits) is not null)
            {
                continue;
            }

            if (best is null || Distance(start, session.Start) < Distance(best.Start, session.Start))
            {
                best = session with { Start = start };
            }
        }

        return best;
    }

    private static TimeSpan Distance(DateTimeOffset left, DateTimeOffset right) => (left - right).Duration();

    private static PlannedSession? Indoors(WorkoutPlanRequest request, PlannedSession session)
    {
        var hasTreadmill = request.Equipment.Has(EquipmentCatalogue.TreadmillId);
        var original = session.Session;
        if (original.Kind == SessionKind.GentleWalk)
        {
            return hasTreadmill && !session.IsJoint
                ? session with { Session = new Session(SessionKind.GentleWalk, original.Duration, false, [new Exercise("Easy walk on the treadmill", EquipmentCatalogue.TreadmillId)]) }
                : null;
        }

        var kind = hasTreadmill ? SessionKind.Treadmill : SessionKind.Mobility;
        var weekIndex = request.WeekStart.DayNumber / 7;
        var duration = SessionLibrary.DurationFor(kind, session.Together, original.Duration);
        return session with { Session = SessionLibrary.Build(kind, session.Together, duration, request.Equipment, weekIndex) };
    }
}

/// <summary>Rebuilds a week with some sessions replaced or removed, keeping a joint session one object in every plan.</summary>
internal static class WeekRewrite
{
    /// <summary>Every distinct session in the plan, by reference: a joint session once, however many plans hold it.</summary>
    public static List<PlannedSession> Distinct(HouseholdWorkoutPlan plan) =>
        plan.Plans.SelectMany(week => week.Sessions).Concat(plan.JointSessions).Distinct<PlannedSession>(ReferenceEqualityComparer.Instance).ToList();

    /// <summary>
    /// <paramref name="outcomes"/> maps a session (by reference) to its replacement, or to null to remove it; a session it does
    /// not hold is kept. <paramref name="reason"/> gives the plan's NothingBecause from the plan and the sessions it now holds;
    /// it is asked only of plans that changed.
    /// </summary>
    public static HouseholdWorkoutPlan Apply(
        HouseholdWorkoutPlan plan,
        IReadOnlyDictionary<PlannedSession, PlannedSession?> outcomes,
        Func<WeeklyPlan, IReadOnlyList<PlannedSession>, string?> reason)
    {
        PlannedSession? Map(PlannedSession session) => outcomes.TryGetValue(session, out var replacement) ? replacement : session;

        var plans = plan.Plans.Select(week =>
        {
            if (!week.Sessions.Any(outcomes.ContainsKey))
            {
                return week;
            }

            var kept = week.Sessions.Select(Map).OfType<PlannedSession>().OrderBy(session => session.Start).ToList();
            return new WeeklyPlan(week.Person, week.WeekStart, kept, reason(week, kept));
        }).ToList();
        var joint = plan.JointSessions.Select(Map).OfType<PlannedSession>().ToList();
        return new HouseholdWorkoutPlan(plan.WeekStart, plans, joint);
    }
}
