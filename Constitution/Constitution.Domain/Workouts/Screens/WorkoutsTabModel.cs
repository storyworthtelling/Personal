namespace Constitution.Domain.Workouts.Screens;

/// <summary>What a day on the tab is.</summary>
public enum DayState
{
    Session,
    Rest,
    NotPlanned,
}

/// <summary>
/// One clipping on the tab, as words. Today's is the large one (kicker, headline, kit and Score lines); each later day is a
/// small one (headline is "time · length · type", then Score, then a reason if the planner changed something).
/// </summary>
public sealed record DayClipping(
    DateOnly Day,
    bool IsToday,
    DayState State,
    bool IsSkipped,
    string DayLabel,
    string LongDay,
    string? Kicker,
    string Headline,
    string? KitLine,
    string? ScoreLine,
    string? ReasonLine,
    string? SkippedNote,
    string KindWord,
    string StartText,
    int Minutes,
    bool OnCalendar,
    bool CanPress);

/// <summary>A skip that is waiting out its undo window; the tab draws its day struck through meanwhile.</summary>
public sealed record PendingSkip(
    string Person,
    DateOnly Day,
    SessionKind Kind,
    PlannedSession Session,
    PlacedTime? OnCalendar,
    Constitution.Domain.Placement.PlacementKey? Key,
    DateTimeOffset Expires);

/// <summary>The Workouts tab (C-5.4.1, book p.12): today large, then the next seven days. Every decision is here; the head paints.</summary>
public sealed class WorkoutsTabModel
{
    public const string StatusSentence = "Today, then the next seven days";

    public const string NoPlanSentence = "No workouts are planned yet.";

    private WorkoutsTabModel(DayClipping? today, IReadOnlyList<DayClipping> days, string status)
    {
        Today = today;
        Days = days;
        Status = status;
    }

    public DayClipping? Today { get; }

    public IReadOnlyList<DayClipping> Days { get; }

    public string Status { get; }

    public bool HasPlan => Today is not null;

    public static WorkoutsTabModel NoPlan() => new(null, [], NoPlanSentence);

    /// <summary>
    /// The line under the tab's header: the sentence about a change that did not go through cleanly (<see cref="FollowWords"/>) while there is
    /// one, otherwise the usual <see cref="StatusSentence"/>. An inline state, in words, in the place the page already says its status.
    /// </summary>
    public static string StatusWith(string? notice) => string.IsNullOrWhiteSpace(notice) ? StatusSentence : notice;

    /// <param name="snapshot">What the tab is made from.</param>
    /// <param name="pending">The skips on show as skipped: those inside their undo window and those whose window has closed but which are not yet written down (<see cref="SkipUndo.Shown"/>).</param>
    public static WorkoutsTabModel Build(WorkoutsSnapshot snapshot, IReadOnlyCollection<PendingSkip>? pending)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var today = Clip(snapshot, snapshot.Today, pending);
        var days = Enumerable.Range(1, DaysAhead).Select(offset => Clip(snapshot, snapshot.Today.AddDays(offset), pending)).ToList();
        return new WorkoutsTabModel(today, days, StatusSentence);
    }

    private const int DaysAhead = 7;

    private static DayClipping Clip(WorkoutsSnapshot snapshot, DateOnly day, IReadOnlyCollection<PendingSkip>? pending)
    {
        var isToday = day == snapshot.Today;
        var label = WorkoutWords.ShortDay(day);
        var longDay = WorkoutWords.LongDay(day);
        var plan = snapshot.PersonPlanOf(day);
        var planned = plan is not null && !(plan.Sessions.Count == 0 && plan.NothingBecause is not null);
        if (!planned)
        {
            return new DayClipping(
                day, isToday, DayState.NotPlanned, false, label, longDay, isToday ? "Today" : null, "Not planned yet",
                null, null, null, null, "Not planned", string.Empty, 0, false, false);
        }

        var session = snapshot.SessionOn(day);
        if (session is null)
        {
            return new DayClipping(
                day, isToday, DayState.Rest, false, label, longDay, isToday ? "Today" : null, "Rest",
                null, null, null, null, "Rest", string.Empty, 0, false, true);
        }

        var facts = snapshot.FactsOn(day);
        var skipped = snapshot.IsSkipped(day) || (pending?.Any(skip => skip.Day == day) ?? false);
        var kind = WorkoutWords.Kind(session);
        var minutes = WorkoutWords.Minutes(session);
        var start = WorkoutWords.Clock(session.Start);
        var score = ScoreLine(session, snapshot.Person, facts, isToday);
        var reason = facts.ChangeReason
            ?? (day.DayOfWeek == DayOfWeek.Sunday && session.Session.Kind == SessionKind.GentleWalk ? "Sundays are gentle walks" : null);

        return new DayClipping(
            day,
            isToday,
            DayState.Session,
            skipped,
            label,
            longDay,
            isToday ? $"Today · {start}" : null,
            isToday ? $"{kind}, {minutes} minutes" : $"{start} · {minutes} min · {kind}",
            isToday ? WorkoutWords.Kit(session.Session) : null,
            score,
            isToday ? null : reason,
            skipped ? "Skipped · counted as a rest" : null,
            kind,
            start,
            minutes,
            facts.OnCalendar is not null,
            !skipped);
    }

    private static string? ScoreLine(PlannedSession session, string person, DayFacts facts, bool isToday)
    {
        var parts = new List<string>();
        if (facts.ScoreBpm is { } bpm)
        {
            parts.Add(WorkoutWords.Score(bpm));
        }
        else if (session.Session.Kind == SessionKind.GentleWalk)
        {
            parts.Add("Gentle");
        }

        if (WorkoutWords.With(session, person) is { } together)
        {
            parts.Add(together);
        }

        if (isToday)
        {
            parts.Add(facts.PagesOnTablet ? "on your tablet" : "not on your tablet yet");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
