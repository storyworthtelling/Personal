using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Placement;

/// <summary>
/// The invented week the placement tests stand on: the planner fixture's Monday 2 November 2026 (all on
/// standard time, UTC-5), with Sam and Lee. Sam works 8 to 4 and Lee 8 to 3:30 on weekdays, dinner is 6 to 7
/// and the children's routine runs 7:15 to 8, so on a weekday Sam's free afternoon is 4 to 6 pm. Sessions
/// here are built by hand rather than asked of the planner, so each test controls exactly the gap it is
/// about; one test at the end of <see cref="PlacementTests"/> runs the real planner's output through.
/// Every expected instant is written out on the clock.
/// </summary>
internal static class PlacementFixture
{
    public static readonly TimeSpan ShowerBuffer = TimeSpan.FromMinutes(15);

    public static readonly int Week = PlacementKey.WeekIndexOf(WeekStart);

    public static Session Effort(int minutes, bool sweaty = true, SessionKind kind = SessionKind.Strength) =>
        new(kind, TimeSpan.FromMinutes(minutes), sweaty, [new Exercise("Sit to stand from a chair")]);

    /// <summary>A session proposed at the start of its gap, which is what the planner does.</summary>
    public static PlannedSession Planned(int dayOffset, (int Hour, int Minute) gapFrom, (int Hour, int Minute) gapTo, Session session, params string[] participants) =>
        PlannedAt(dayOffset, gapFrom, gapTo, gapFrom, session, Together.None, participants);

    public static PlannedSession PlannedAt(
        int dayOffset,
        (int Hour, int Minute) gapFrom,
        (int Hour, int Minute) gapTo,
        (int Hour, int Minute) proposed,
        Session session,
        Together together,
        params string[] participants) =>
        new(
            Day(dayOffset),
            At(dayOffset, proposed.Hour, proposed.Minute),
            new FreeGap(At(dayOffset, gapFrom.Hour, gapFrom.Minute), At(dayOffset, gapTo.Hour, gapTo.Minute)),
            session,
            participants,
            together);

    /// <summary>
    /// A week plan from sessions. A joint session is one object in every participant's list and in
    /// <see cref="HouseholdWorkoutPlan.JointSessions"/>, the way the planner hands it over.
    /// </summary>
    public static HouseholdWorkoutPlan PlanOf(params PlannedSession[] sessions)
    {
        var people = sessions.SelectMany(session => session.Participants).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        var plans = people
            .Select(person => new WeeklyPlan(
                person,
                WeekStart,
                sessions.Where(session => session.Participants.Contains(person)).OrderBy(session => session.Start).ToList(),
                null))
            .ToList();
        return new HouseholdWorkoutPlan(WeekStart, plans, sessions.Where(session => session.IsJoint).ToList());
    }

    /// <summary>A household event for the given people, in one window, on one day.</summary>
    public static HouseholdEventRequest Household(
        string identity,
        string title,
        int minutes,
        int dayOffset,
        (int Hour, int Minute) from,
        (int Hour, int Minute) to,
        params string[] people) =>
        new(
            identity,
            title,
            TimeSpan.FromMinutes(minutes),
            people,
            [new PlacementWindow(At(dayOffset, from.Hour, from.Minute), At(dayOffset, to.Hour, to.Minute))]);

    public static HouseholdEventRequest Cook(string person, int dayOffset, (int Hour, int Minute) from, (int Hour, int Minute) to, int minutes) =>
        Household("dinner-a", "Cook: chili", minutes, dayOffset, from, to, person);

    public static PlacementRequest PlaceRequest(HouseholdCalendarSnapshot calendars, HouseholdWorkoutPlan? workouts = null, params HouseholdEventRequest[] household) =>
        new(WeekStart, [BusyFor(Sam, calendars), BusyFor(Lee, calendars)], household, workouts)
        {
            Options = new PlacementOptions { ShowerBuffer = ShowerBuffer },
        };

    public static CalendarState Fresh { get; } = new(WrittenKeys.Empty, [], new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public static PlacementPlan PlaceWeek(HouseholdCalendarSnapshot calendars, HouseholdWorkoutPlan? workouts = null, params HouseholdEventRequest[] household) =>
        PlacementEngine.Plan(PlaceRequest(calendars, workouts, household), Fresh);

    public static PlacementKey StrengthKey(string person, int ordinal = 0) => PlacementKey.ForWorkout(person, SessionKind.Strength, Week, ordinal);

    public static PlacementKey MobilityKey(string person, int ordinal = 0) => PlacementKey.ForWorkout(person, SessionKind.Mobility, Week, ordinal);

    public static PlacementKey HouseholdKey(string person, string identity) => PlacementKey.ForHousehold(person, Week, identity);

    public static EventToCreate Made(PlacementPlan plan, string title, string person) =>
        Assert.Single(plan.ToCreate, item => item.Title == title && item.Person == person);
}
