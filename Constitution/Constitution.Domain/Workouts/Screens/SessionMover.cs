using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;

namespace Constitution.Domain.Workouts.Screens;

/// <summary>
/// The one judge of "may this session go to that day", shared by Swap with… and Move a workout here…. It finds a place the
/// way the planner does (the free gaps of the day, in waking hours, before the children's bedtime, the roomiest first) and
/// then holds the changed weeks to <see cref="PlanRules"/>; only a rule the change newly breaks counts against it, so an
/// old problem in the plan never greys a day that is innocent of it.
/// </summary>
internal static class SessionMover
{
    private const double RoomCapMinutes = 60;

    public static bool IsPlanned(WorkoutsSnapshot snapshot, DateOnly day) =>
        snapshot.PersonPlanOf(day) is { } plan && !(plan.Sessions.Count == 0 && plan.NothingBecause is not null);

    public static MoveCheck Check(WorkoutsSnapshot snapshot, DateOnly from, DateOnly to)
    {
        if (from == to)
        {
            return Blocked("that is the same day");
        }

        if (snapshot.SessionOn(from) is not { } mover)
        {
            return Blocked("nothing is planned that day");
        }

        if (!IsPlanned(snapshot, to))
        {
            return Blocked("not planned yet");
        }

        var other = snapshot.SessionOn(to);
        if (snapshot.IsSkipped(from) || snapshot.IsSkipped(to))
        {
            return Blocked("skipped");
        }

        if (mover.IsJoint || (other?.IsJoint ?? false))
        {
            return Blocked("shared with everyone");
        }

        if (mover.Start <= snapshot.Now || (other?.Start <= snapshot.Now))
        {
            return Blocked("already under way");
        }

        var arrived = Place(snapshot, mover, to, out var needed);
        if (arrived is null)
        {
            return Blocked($"no {Minutes(needed)}-minute gap");
        }

        PlannedSession? returned = null;
        if (other is not null)
        {
            returned = Place(snapshot, other, from, out var neededBack);
            if (returned is null)
            {
                return Blocked($"no {Minutes(neededBack)}-minute gap on {WorkoutWords.LongDay(from)}");
            }
        }

        var moves = new List<SessionMove> { new(mover, arrived, snapshot.FactsOn(from).OnCalendar) };
        if (other is not null)
        {
            moves.Add(new SessionMove(other, returned!, snapshot.FactsOn(to).OnCalendar));
        }

        var before = Affected(snapshot, moves);
        var after = before.Select(week => Edit(week, snapshot.Person, moves)).ToList();
        var reason = FirstNewBreak(snapshot, before, after);
        return reason is null
            ? new MoveCheck(new SwapResult(snapshot.Person, moves, before, after), null)
            : Blocked(reason);
    }

    private static MoveCheck Blocked(string reason) => new(null, reason);

    private static int Minutes(TimeSpan span) => (int)Math.Round(span.TotalMinutes);

    // ---- where it goes ----

    private static PlannedSession? Place(WorkoutsSnapshot snapshot, PlannedSession session, DateOnly day, out TimeSpan needed)
    {
        var room = session.Session.Duration + (session.Session.IsSweaty ? PlacementOptions.DefaultShowerBuffer : TimeSpan.Zero);
        needed = room;
        var week = snapshot.WeekOf(day);
        var busy = week?.Request.People.FirstOrDefault(item => Same(item.Busy.Person, snapshot.Person))?.Busy;
        if (week is null || busy is null)
        {
            return null;
        }

        var request = week.Request;
        var zone = request.Rules.Zone;
        var opens = ZoneClock.At(day, request.Settings.WakingStart, zone, ClockEdge.Start);
        var closes = ZoneClock.At(day, request.Settings.WakingEnd, zone, ClockEdge.End);
        var from = snapshot.Now > opens ? snapshot.Now : opens;
        if (closes <= from)
        {
            return null;
        }

        IEnumerable<FreeGap> gaps = busy.FreeGaps(from, closes, WorkoutPlanner.MinimumGap);
        if (!request.Rules.Bedtime.IsNone)
        {
            var bedtime = ZoneClock.At(day, request.Rules.Bedtime.Bedtime, zone, ClockEdge.End);
            gaps = gaps.Select(gap => new FreeGap(gap.Start, gap.End < bedtime ? gap.End : bedtime))
                .Where(gap => gap.Length >= WorkoutPlanner.MinimumGap);
        }

        var best = gaps
            .Where(gap => gap.Length >= room)
            .OrderByDescending(gap => Math.Min(gap.Length.TotalMinutes, RoomCapMinutes))
            .ThenByDescending(gap => gap.Start)
            .FirstOrDefault();
        return best is null ? null : session with { Day = day, Start = best.Start, Gap = best };
    }

    // ---- the weeks, before and after ----

    private static List<WorkoutWeek> Affected(WorkoutsSnapshot snapshot, IReadOnlyList<SessionMove> moves) =>
        moves.SelectMany(move => new[] { move.Old.Day, move.New.Day })
            .Select(day => snapshot.WeekOf(day)!)
            .Distinct()
            .OrderBy(week => week.Plan.WeekStart)
            .ToList();

    private static WorkoutWeek Edit(WorkoutWeek week, string person, IReadOnlyList<SessionMove> moves)
    {
        var plans = week.Plan.Plans.Select(plan =>
        {
            if (!Same(plan.Person, person))
            {
                return plan;
            }

            var sessions = plan.Sessions
                .Where(session => !moves.Any(move => ReferenceEquals(move.Old, session)))
                .Concat(moves.Select(move => move.New).Where(session => InWeek(week, session.Day)))
                .OrderBy(session => session.Start)
                .ToList();
            return plan with { Sessions = sessions };
        }).ToList();
        return week with { Plan = week.Plan with { Plans = plans } };
    }

    private static bool InWeek(WorkoutWeek week, DateOnly day) => day >= week.Plan.WeekStart && day < week.Plan.WeekStart.AddDays(7);

    // ---- the rules ----

    private static string? FirstNewBreak(WorkoutsSnapshot snapshot, IReadOnlyList<WorkoutWeek> before, IReadOnlyList<WorkoutWeek> after)
    {
        var broken = new List<PlanRule>();
        for (var index = 0; index < before.Count; index++)
        {
            var request = before[index].Request with { Now = snapshot.Now };
            var already = PlanRules.Violations(request, before[index].Plan);
            broken.AddRange(PlanRules.Violations(request, after[index].Plan).Except(already).Select(item => item.Rule));
        }

        if (broken.Count == 0)
        {
            return null;
        }

        return Words(broken.OrderBy(Priority).First());
    }

    private static int Priority(PlanRule rule) => rule switch
    {
        PlanRule.SundayNotGentle => 0,
        PlanRule.HardOnConsecutiveDays => 1,
        PlanRule.UntickedEquipment => 2,
        PlanRule.InThePast => 3,
        PlanRule.TwoOnOneDay => 4,
        _ => 5,
    };

    private static string Words(PlanRule rule) => rule switch
    {
        PlanRule.SundayNotGentle => "Sundays are gentle walks",
        PlanRule.HardOnConsecutiveDays => "two hard days in a row",
        PlanRule.UntickedEquipment => "needs equipment that is not ticked",
        PlanRule.InThePast => "that time has already passed",
        PlanRule.TwoOnOneDay => "already has a workout",
        PlanRule.NotInTheRequest or PlanRule.OutsideTheWeek => "outside the weeks planned",
        PlanRule.SessionLeavesItsGap or PlanRule.GapTooShort or PlanRule.BusyTime or PlanRule.OutsideWakingHours => "no room that day",
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "The tab has no words for this rule."),
    };

    private static bool Same(string left, string right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
