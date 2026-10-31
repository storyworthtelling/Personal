using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Screens;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// What turning the saved week back into the tab's snapshot needs besides the record. <paramref name="Request"/> is the week's request as the
/// household stands now (the rules, the equipment and each adult's busy time), because the saved week is compact and holds none of it;
/// the tab needs it to check a swap against the same rules the planner keeps. <paramref name="PagesDelivered"/> says whether the owner's page
/// for a day has gone to the tablet.
/// </summary>
public sealed record WeekReadInputs(
    string Person,
    DateTimeOffset Now,
    WorkoutPlanRequest Request,
    KeyAssignments Assignments,
    SkipLedger Skips,
    Func<DateOnly, bool> PagesDelivered);

/// <summary>
/// The saved week read back into <see cref="WorkoutsSnapshot"/>. Pure. The sessions are the placed plan the run kept, so the tab shows the
/// plan that went to the calendar. A week that has ended is no plan yet (never an empty plan that looks like one), and a record with a
/// session that cannot be rebuilt is unavailable with a fixed sentence, never a shorter week.
/// </summary>
public static class WeekRecordSnapshot
{
    public const string SessionUnreadable = "The saved week has a session that could not be read.";

    public static WorkoutsRead From(StoredWorkoutWeek week, WeekReadInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(inputs);

        var today = ZoneClock.DayOf(inputs.Now, inputs.Request.Rules.Zone);
        if (today >= week.WeekStart.AddDays(7))
        {
            return new WorkoutsRead.NoPlanYet();
        }

        var built = new List<(StoredSession Stored, PlannedSession Planned)>();
        foreach (var stored in week.Sessions)
        {
            if (Build(stored) is not { } planned)
            {
                return new WorkoutsRead.Unavailable(SessionUnreadable);
            }

            built.Add((stored, planned));
        }

        var joint = JointOnes(built);
        var plans = PeopleOf(week, inputs).Select(person =>
        {
            var own = built
                .Where(item => Same(item.Stored.Person, person) && item.Planned.Together == Together.None)
                .Select(item => item.Planned)
                .Concat(joint.Where(item => item.Participants.Any(name => Same(name, person)) || built.Any(b => Same(b.Stored.Person, person) && ReferenceEquals(b.Planned, item))))
                .OrderBy(item => item.Start)
                .ToList();
            return new WeeklyPlan(person, week.WeekStart, own, null);
        }).ToList();

        var request = inputs.Request;
        var snapshot = new WorkoutsSnapshot(
            inputs.Person,
            today,
            inputs.Now,
            [new WorkoutWeek(request, new HouseholdWorkoutPlan(week.WeekStart, plans, joint))],
            FactsOf(week, inputs),
            inputs.Skips)
        {
            Assignments = inputs.Assignments,
        };
        return new WorkoutsRead.Ready(snapshot);
    }

    private static PlannedSession? Build(StoredSession stored)
    {
        if (WorkoutWireNames.SessionKindOf(stored.Kind) is not { } kind
            || stored.Minutes <= 0
            || stored.Exercises.Count == 0
            || stored.Exercises.Any(string.IsNullOrWhiteSpace)
            || !Enum.TryParse<Together>(stored.Together, out var together))
        {
            return null;
        }

        var session = new Session(kind, TimeSpan.FromMinutes(stored.Minutes), stored.IsSweaty, stored.Exercises.Select(name => new Exercise(name)).ToList());
        return new PlannedSession(stored.Day, stored.Start, new FreeGap(stored.Start, stored.Start + session.Duration), session, stored.Participants.ToList(), together);
    }

    /// <summary>A shared session is one object in every participant's plan, as the planner made it.</summary>
    private static List<PlannedSession> JointOnes(List<(StoredSession Stored, PlannedSession Planned)> built)
    {
        var shared = new List<PlannedSession>();
        for (var index = 0; index < built.Count; index++)
        {
            var item = built[index];
            if (item.Planned.Together == Together.None)
            {
                continue;
            }

            var first = shared.FirstOrDefault(other => other.Day == item.Planned.Day && other.Start == item.Planned.Start && other.Session.Kind == item.Planned.Session.Kind);
            if (first is null)
            {
                shared.Add(item.Planned);
            }
            else
            {
                built[index] = (item.Stored, first);
            }
        }

        return shared;
    }

    private static List<string> PeopleOf(StoredWorkoutWeek week, WeekReadInputs inputs)
    {
        var names = new List<string>();
        foreach (var name in week.Sessions.Select(item => item.Person)
                     .Concat(week.Sessions.SelectMany(item => item.Participants))
                     .Concat(inputs.Request.People.Select(item => item.Busy.Person))
                     .Append(inputs.Person))
        {
            if (!names.Any(known => Same(known, name)))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static Dictionary<DateOnly, DayFacts> FactsOf(StoredWorkoutWeek week, WeekReadInputs inputs)
    {
        var facts = new Dictionary<DateOnly, DayFacts>();
        foreach (var stored in week.Sessions.Where(item => Same(item.Person, inputs.Person)))
        {
            facts[stored.Day] = new DayFacts(
                stored.ScoreBpm,
                null,
                new PlacedTime(stored.Start, stored.Start + TimeSpan.FromMinutes(stored.Minutes)),
                inputs.PagesDelivered(stored.Day));
        }

        foreach (var reason in week.Reasons.Where(item => Same(item.Person, inputs.Person)).GroupBy(item => item.Day))
        {
            var line = reason.First().Line;
            facts[reason.Key] = facts.TryGetValue(reason.Key, out var held) ? held with { ChangeReason = line } : new DayFacts(ChangeReason: line);
        }

        return facts;
    }

    private static bool Same(string left, string right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>A swap or a move the owner made on the tab, kept in the saved week so the tab reads what the owner left. Pure.</summary>
public static class WeekRecordEdit
{
    /// <exception cref="InvalidOperationException">A moved session is not in the saved week (the run has re-planned since), so there is nothing to change.</exception>
    public static StoredWorkoutWeek Apply(StoredWorkoutWeek week, SwapResult swap)
    {
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(swap);

        var sessions = week.Sessions.ToList();
        var touched = new List<DateOnly>();
        foreach (var move in swap.Moves)
        {
            var index = sessions.FindIndex(item =>
                string.Equals(item.Person, swap.Person, StringComparison.OrdinalIgnoreCase)
                && item.Day == move.Old.Day
                && item.Start == move.Old.Start
                && item.Kind == WorkoutWireNames.Of(move.Old.Session.Kind));
            if (index < 0)
            {
                throw new InvalidOperationException("That session is no longer in the saved week, so it cannot be moved.");
            }

            sessions[index] = sessions[index] with { Day = move.New.Day, Start = move.New.Start };
            touched.Add(move.Old.Day);
            touched.Add(move.New.Day);
        }

        // Today's clipping on a day that changed is read from the moved sessions again, not from the run's earlier word.
        var today = week.Today.Where(item => !(string.Equals(item.Person, swap.Person, StringComparison.OrdinalIgnoreCase) && touched.Contains(item.Day))).ToList();
        return week with { Sessions = sessions, Today = today };
    }
}
