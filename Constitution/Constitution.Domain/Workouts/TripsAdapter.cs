using Constitution.Domain.Equipment;

namespace Constitution.Domain.Workouts;

/// <summary>What was done to a session because someone is away.</summary>
public enum TravelAction
{
    /// <summary>Rebuilt for the road: in the room, outdoors or at the hotel gym.</summary>
    Rebuilt,

    /// <summary>Taken off the plan because the person is on a holiday.</summary>
    Paused,

    /// <summary>A joint session taken off for everyone because one of them is away.</summary>
    DroppedJoint,
}

/// <summary>One session changed for travel. <see cref="Replacement"/> is null when it was taken off.</summary>
public sealed record TravelChange(TravelAction Action, PlannedSession Original, PlannedSession? Replacement, string Line)
{
    public DateOnly Day => Original.Day;

    public IReadOnlyList<string> People => Original.Participants;
}

/// <summary>A line for one person's day on the Workouts tab ("Paused: family holiday").</summary>
public sealed record DayNote(string Person, DateOnly Day, string Line);

/// <summary>The week once travel has had its say.</summary>
public sealed record TravelAdaptation(HouseholdWorkoutPlan Plan, IReadOnlyList<TravelChange> Changes, IReadOnlyList<DayNote> Notes);

/// <summary>The words of the travel lines. Nothing from a calendar, a hotel or a lookup ever reaches a line.</summary>
public static class TravelWords
{
    public const string Paused = "Paused: family holiday";
    public const string InTheRoom = "On the road: in the room";
    public const string Outdoors = "On the road: outdoors";
    public const string HotelGym = "On the road: hotel gym";
    public const string DroppedJoint = "Dropped: someone is away";

    /// <summary>What a person's plan says when a holiday took away every session it had, or all of its days.</summary>
    public const string PausedWeek = "The plan is paused for the family holiday.";

    /// <summary>What a person's plan says when every session it had was a shared one on a day someone was away.</summary>
    public const string AwayWeek = "Every session this week fell on a day someone was away.";
}

/// <summary>
/// Adapts a planned week to the trips (C-5.2.5). The pipeline a head runs: <see cref="TripDetector.Detect"/>, then
/// <see cref="TravelPlan.ResolveAsync"/>, then build each person's <c>BusyTime</c> from <see cref="TripAwareCalendars"/> and
/// plan the week, then this.
///
/// <para><b>A work trip continues, on the road.</b> The home equipment is not in the room, so a strength or mobility session is
/// rebuilt with no equipment; outdoor cardio stays outdoors (and a treadmill session becomes outdoor cardio); a gentle walk is
/// left alone. If the calendar names a hotel and the lookup <i>found</i> a gym there, strength and mobility use what the gym
/// holds and cardio goes to its treadmill if it has one. A gym that was not found, or not confirmed, is never prescribed. The
/// slot and the length are kept, so a road session fits wherever the original did. Road sessions are the one place a session may
/// use equipment that is not on the home checklist (D72): the checklist is what the household owns, and a hotel gym is not it.</para>
///
/// <para><b>A holiday pauses.</b> Every session of the person on a holiday day is taken off, and every holiday day in the week
/// gets a note for the Workouts tab, session or none. The other days of the week are not touched and nothing is moved onto
/// them: a paused plan is a smaller one, not a harder one.</para>
///
/// <para><b>A joint session goes when anyone in it is away</b> (on any trip): a family walk or an evening together needs everyone at home. The
/// other adult's own session that the joint one replaced is not put back; fewer sessions is never a broken rule.</para>
/// </summary>
public static class TripAdapter
{
    private const int DaysInWeek = 7;

    public static TravelAdaptation Apply(WorkoutPlanRequest request, HouseholdWorkoutPlan plan, TravelPlan travel)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(travel);

        var days = Enumerable.Range(0, DaysInWeek).Select(plan.WeekStart.AddDays).ToList();
        var outcomes = new Dictionary<PlannedSession, PlannedSession?>(ReferenceEqualityComparer.Instance);
        var changes = new List<TravelChange>();
        foreach (var session in WeekRewrite.Distinct(plan).OrderBy(item => item.Start))
        {
            if (Decide(plan, travel, session) is { } change)
            {
                changes.Add(change);
                if (change.Replacement is not { } replacement || !ReferenceEquals(replacement, session))
                {
                    outcomes[session] = change.Replacement;
                }
            }
        }

        var adapted = outcomes.Count == 0
            ? plan
            : WeekRewrite.Apply(plan, outcomes, (week, kept) => EmptiedBecause(request, travel, days, week, kept, changes));
        var withNotes = Notes(adapted, travel, days);
        var emptyWeeks = AlreadyEmptyPaused(request, adapted, travel, days);
        return new TravelAdaptation(emptyWeeks ?? adapted, changes, withNotes);
    }

    private static TravelChange? Decide(HouseholdWorkoutPlan plan, TravelPlan travel, PlannedSession session)
    {
        var trips = session.Participants.Select(person => (Person: person, Trip: travel.On(person, session.Day))).Where(item => item.Trip is not null).ToList();
        if (trips.Count == 0)
        {
            return null;
        }

        if (session.IsJoint || session.Participants.Count > 1)
        {
            return new TravelChange(TravelAction.DroppedJoint, session, null, TravelWords.DroppedJoint);
        }

        var trip = trips[0].Trip!;
        if (trip.Kind == TripKind.FamilyHoliday)
        {
            return new TravelChange(TravelAction.Paused, session, null, TravelWords.Paused);
        }

        var rebuilt = Rebuild(session, travel.GymFor(trip), plan.WeekStart);
        return ReferenceEquals(rebuilt, session)
            ? null
            : new TravelChange(TravelAction.Rebuilt, session, rebuilt, PlaceLine(rebuilt, travel.GymFor(trip) is not null));
    }

    // The session as it is done on the road. Variant 0 takes the richest option the kit allows for each pattern, which in a
    // room is the bodyweight one; there is no variety to keep for a few days away.
    private static PlannedSession Rebuild(PlannedSession session, EquipmentSet? gym, DateOnly weekStart)
    {
        var kit = gym ?? EquipmentSet.Of([]);
        var original = session.Session;
        var kind = original.Kind;
        switch (kind)
        {
            case SessionKind.Strength:
            case SessionKind.Mobility:
                return session with { Session = SessionLibrary.Build(kind, Together.None, original.Duration, kit, 0) };
            case SessionKind.OutdoorCardio when gym?.Has(EquipmentCatalogue.TreadmillId) == true:
            case SessionKind.Treadmill when gym?.Has(EquipmentCatalogue.TreadmillId) != true:
                var target = gym?.Has(EquipmentCatalogue.TreadmillId) == true ? SessionKind.Treadmill : SessionKind.OutdoorCardio;
                return session with { Session = SessionLibrary.Build(target, Together.None, original.Duration, kit, 0) };
            default:
                return session;
        }
    }

    private static string PlaceLine(PlannedSession session, bool gymFound) => session.Session.Kind switch
    {
        SessionKind.OutdoorCardio or SessionKind.GentleWalk => TravelWords.Outdoors,
        SessionKind.Treadmill => TravelWords.HotelGym,
        _ => gymFound ? TravelWords.HotelGym : TravelWords.InTheRoom,
    };

    private static string? EmptiedBecause(
        WorkoutPlanRequest request,
        TravelPlan travel,
        IReadOnlyList<DateOnly> days,
        WeeklyPlan week,
        IReadOnlyList<PlannedSession> kept,
        IReadOnlyList<TravelChange> changes)
    {
        if (kept.Count > 0 || !IsReadable(request, week.Person))
        {
            return week.NothingBecause;
        }

        var paused = changes.Any(change => change.Action == TravelAction.Paused && change.People.Contains(week.Person));
        return paused ? TravelWords.PausedWeek : TravelWords.AwayWeek;
    }

    // A week that had nothing to begin with (the calendar was full) and is paused on every day of it: the holiday is the reason.
    private static HouseholdWorkoutPlan? AlreadyEmptyPaused(WorkoutPlanRequest request, HouseholdWorkoutPlan plan, TravelPlan travel, IReadOnlyList<DateOnly> days)
    {
        var allPaused = plan.Plans.Where(week =>
            week.Sessions.Count == 0
            && IsReadable(request, week.Person)
            && days.All(day => travel.On(week.Person, day)?.Kind == TripKind.FamilyHoliday)).ToList();
        if (allPaused.Count == 0)
        {
            return null;
        }

        var plans = plan.Plans.Select(week => allPaused.Contains(week) ? new WeeklyPlan(week.Person, week.WeekStart, week.Sessions, TravelWords.PausedWeek) : week).ToList();
        return new HouseholdWorkoutPlan(plan.WeekStart, plans, plan.JointSessions);
    }

    private static bool IsReadable(WorkoutPlanRequest request, string person) =>
        request.People.FirstOrDefault(item => string.Equals(item.Busy.Person, person, StringComparison.OrdinalIgnoreCase))?.Busy.IsComplete ?? true;

    private static List<DayNote> Notes(HouseholdWorkoutPlan plan, TravelPlan travel, IReadOnlyList<DateOnly> days)
    {
        var notes = new List<DayNote>();
        foreach (var day in days)
        {
            foreach (var person in plan.Plans.Select(week => week.Person).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                var trip = travel.On(person, day);
                if (trip is null)
                {
                    continue;
                }

                if (trip.Kind == TripKind.FamilyHoliday)
                {
                    notes.Add(new DayNote(person, day, TravelWords.Paused));
                }
                else if (plan.PlanFor(person)!.SessionsOn(day).FirstOrDefault() is { } session)
                {
                    notes.Add(new DayNote(person, day, PlaceLine(session, travel.GymFor(trip) is not null)));
                }
            }
        }

        return notes;
    }
}
