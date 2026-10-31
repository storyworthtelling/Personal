using Constitution.Domain.Calendar;

namespace Constitution.Domain.Workouts;

/// <summary>
/// Why the preferred author gave no usable plan. A closed list, never a sentence: the reason is recorded for
/// Diagnostics and must not carry anything the author or the network said.
/// </summary>
public enum AuthorUnavailable
{
    /// <summary>No author is set up (no key, or not built yet).</summary>
    NotConfigured,

    /// <summary>The service could not be reached.</summary>
    Offline,

    /// <summary>It did not answer in time.</summary>
    TimedOut,

    /// <summary>It refused the credential.</summary>
    KeyRejected,

    /// <summary>It answered with something that is not a plan.</summary>
    ReplyUnusable,

    /// <summary>It answered with a plan that broke the rules (<see cref="PlanRules"/>).</summary>
    BrokeTheRules,
}

/// <summary>What an author hands back: a plan, or the plain fact that it has none.</summary>
public abstract record PlanAuthorResult
{
    public sealed record Authored(HouseholdWorkoutPlan Plan) : PlanAuthorResult;

    public sealed record Unavailable(AuthorUnavailable Reason) : PlanAuthorResult;
}

/// <summary>
/// The seam where a plan written by the assistant plugs in (R10). An implementation lives in Infrastructure, does the
/// I/O, and turns every failure it can name into <see cref="PlanAuthorResult.Unavailable"/>; an exception it does not
/// understand is a bug and is allowed to propagate (<see cref="PlanWithFallback"/> does not swallow it).
/// </summary>
public interface IPlanAuthor
{
    Task<PlanAuthorResult> AuthorAsync(WorkoutPlanRequest request, CancellationToken cancellation);
}

/// <summary>The author used until there is one: every week is rules-only, and the page cannot tell.</summary>
public sealed class NoPlanAuthor : IPlanAuthor
{
    public Task<PlanAuthorResult> AuthorAsync(WorkoutPlanRequest request, CancellationToken cancellation) =>
        Task.FromResult<PlanAuthorResult>(new PlanAuthorResult.Unavailable(AuthorUnavailable.NotConfigured));
}

/// <summary>Where a plan came from. Kept on the outcome and in Diagnostics, never on the plan the page is made from.</summary>
public enum PlanOrigin
{
    Authored,

    RulesOnly,
}

/// <summary>The one place the origin is written down (Diagnostics). It never reaches a page, a notice or a voiced line.</summary>
public interface IPlanLog
{
    void Record(PlanOrigin origin, AuthorUnavailable? reason);
}

public sealed record PlanOutcome(HouseholdWorkoutPlan Plan, PlanOrigin Origin, AuthorUnavailable? Reason);

/// <summary>
/// Asks the preferred author for the week and holds what it says to <see cref="PlanRules"/>; if there is no plan, or
/// it breaks a rule, the rules planner's week is used instead. The page cannot tell: the returned plan is the same
/// kind of object either way, and the origin goes to the log and to the outcome only.
/// </summary>
public sealed class PlanWithFallback
{
    private readonly IPlanAuthor preferred;
    private readonly IPlanLog log;

    public PlanWithFallback(IPlanAuthor preferred, IPlanLog log)
    {
        this.preferred = preferred ?? throw new ArgumentNullException(nameof(preferred));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<PlanOutcome> PlanAsync(WorkoutPlanRequest request, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.People is null || request.People.Count == 0)
        {
            throw new ArgumentException("There is no one to plan for.", nameof(request));
        }

        var result = await preferred.AuthorAsync(request, cancellation);
        var reason = result switch
        {
            PlanAuthorResult.Authored authored when PlanRules.Violations(request, authored.Plan).Count == 0 => (AuthorUnavailable?)null,
            PlanAuthorResult.Authored => AuthorUnavailable.BrokeTheRules,
            PlanAuthorResult.Unavailable unavailable => unavailable.Reason,
            _ => throw new InvalidOperationException("The plan author returned neither a plan nor a reason."),
        };

        var outcome = reason is null
            ? new PlanOutcome(((PlanAuthorResult.Authored)result).Plan, PlanOrigin.Authored, null)
            : new PlanOutcome(WorkoutPlanner.Plan(request), PlanOrigin.RulesOnly, reason);
        log.Record(outcome.Origin, outcome.Reason);
        return outcome;
    }
}

/// <summary>Which rule a plan breaks.</summary>
public enum PlanRule
{
    SessionLeavesItsGap,
    GapTooShort,
    BusyTime,
    OutsideWakingHours,
    InThePast,
    UntickedEquipment,
    SundayNotGentle,
    TwoOnOneDay,
    HardOnConsecutiveDays,
    NotInTheRequest,
    OutsideTheWeek,
}

public sealed record PlanViolation(string Person, DateOnly Day, PlanRule Rule);

/// <summary>
/// The rules the planner keeps, as a check any plan can be held to. It is what makes the author a seam and not a hole:
/// a plan written elsewhere is used only if it is within every rule the rules planner promises (the same ones the
/// generated-weeks suite asserts of <see cref="WorkoutPlanner"/>). A plan with fewer sessions than it might have is
/// never a violation; a plan with sessions it should not have always is.
/// </summary>
public static class PlanRules
{
    private const int DaysInWeek = 7;

    public static IReadOnlyList<PlanViolation> Violations(WorkoutPlanRequest request, HouseholdWorkoutPlan plan)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);

        var found = new List<PlanViolation>();
        foreach (var week in plan.Plans)
        {
            var person = request.People.FirstOrDefault(item => string.Equals(item.Busy.Person, week.Person, StringComparison.OrdinalIgnoreCase));
            if (person is null)
            {
                found.Add(new PlanViolation(week.Person, plan.WeekStart, PlanRule.NotInTheRequest));
                continue;
            }

            foreach (var session in week.Sessions)
            {
                CheckSession(request, person, week.Person, session, found);
            }

            CheckDays(person, week, found);
        }

        return found;
    }

    private static void CheckSession(WorkoutPlanRequest request, PersonWeek person, string name, PlannedSession session, List<PlanViolation> found)
    {
        void Add(PlanRule rule) => found.Add(new PlanViolation(name, session.Day, rule));

        var offset = session.Day.DayNumber - request.WeekStart.DayNumber;
        if (offset < 0 || offset >= DaysInWeek)
        {
            Add(PlanRule.OutsideTheWeek);
        }

        if (session.Gap.Length < WorkoutPlanner.MinimumGap)
        {
            Add(PlanRule.GapTooShort);
        }

        if (session.Start < session.Gap.Start || session.End > session.Gap.End)
        {
            Add(PlanRule.SessionLeavesItsGap);
        }

        if (session.End <= session.Start || !person.Busy.IsFree(session.Start, session.End))
        {
            Add(PlanRule.BusyTime);
        }

        var opens = ZoneClock.At(session.Day, request.Settings.WakingStart, request.Rules.Zone, ClockEdge.Start);
        var closes = ZoneClock.At(session.Day, request.Settings.WakingEnd, request.Rules.Zone, ClockEdge.End);
        if (session.Start < opens || session.End > closes)
        {
            Add(PlanRule.OutsideWakingHours);
        }

        if (session.Start < request.Now)
        {
            Add(PlanRule.InThePast);
        }

        if (session.Session.EquipmentNeeded.Any(id => !request.Equipment.Has(id)))
        {
            Add(PlanRule.UntickedEquipment);
        }

        if (session.Day.DayOfWeek == DayOfWeek.Sunday && session.Session.Kind != SessionKind.GentleWalk)
        {
            Add(PlanRule.SundayNotGentle);
        }
    }

    private static void CheckDays(PersonWeek person, WeeklyPlan week, List<PlanViolation> found)
    {
        foreach (var crowded in week.Sessions.GroupBy(item => item.Day).Where(group => group.Count() > 1))
        {
            found.Add(new PlanViolation(week.Person, crowded.Key, PlanRule.TwoOnOneDay));
        }

        var hardDays = week.Sessions.Where(item => SessionKinds.IsHard(item.Session.Kind)).Select(item => item.Day).Distinct().OrderBy(day => day).ToList();
        foreach (var day in hardDays.Where(day => hardDays.Contains(day.AddDays(-1))))
        {
            found.Add(new PlanViolation(week.Person, day, PlanRule.HardOnConsecutiveDays));
        }

        if (person.Recovery.LastHardSessionDay is { } last)
        {
            foreach (var day in hardDays.Where(day => Math.Abs(day.DayNumber - last.DayNumber) < 2 && !hardDays.Contains(day.AddDays(-1))))
            {
                found.Add(new PlanViolation(week.Person, day, PlanRule.HardOnConsecutiveDays));
            }
        }
    }
}

/// <summary>The line Diagnostics prints for how the week was made: a fixed vocabulary, no names, dates or text from anywhere else.</summary>
public static class PlanDiagnostics
{
    public static string Line(PlanOrigin origin, AuthorUnavailable? reason)
    {
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "There is no such plan origin.");
        }

        if (origin == PlanOrigin.Authored)
        {
            return "Workout plan: authored";
        }

        return reason is null ? "Workout plan: rules only" : $"Workout plan: rules only ({Words(reason.Value)})";
    }

    private static string Words(AuthorUnavailable reason) => reason switch
    {
        AuthorUnavailable.NotConfigured => "not configured",
        AuthorUnavailable.Offline => "offline",
        AuthorUnavailable.TimedOut => "timed out",
        AuthorUnavailable.KeyRejected => "key rejected",
        AuthorUnavailable.ReplyUnusable => "reply unusable",
        AuthorUnavailable.BrokeTheRules => "broke the rules",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "There is no such reason."),
    };
}
