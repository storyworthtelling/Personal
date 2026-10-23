using Constitution.Domain.Calendar;

namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>
/// The seam the Android head fills from Google Calendar for the weekly run: every calendar the planner reads, for the
/// local days <c>[from, to)</c>, as one immutable <see cref="HouseholdCalendarSnapshot"/> (the head does the network; the
/// pure code never waits on anything).
///
/// <para><b>Two rules the implementation must keep, and nothing downstream can check them.</b></para>
/// <list type="number">
///   <item><b>Drop every event that carries the <c>constitutionKey</c> private extended property</b>
///   (<see cref="Constitution.Domain.Placement.PlacementKey.Property"/>). Those are the workouts this app wrote itself.
///   Left in, the planner reads its own earlier placements as busy time, the next run plans around them, the plan
///   changes, and the keys (which count sessions in the plan) move: the week would never settle. Dropping them is what
///   lets a re-run on an unchanged week come out identical.</item>
///   <item><b>Never hand a declined or cancelled event over as <see cref="Availability.Free"/>: leave it out.</b>
///   The trip detector reads the entry whatever its availability (Google's all-day events default to free, and a
///   holiday marked free is still a holiday), so a declined "Holiday" or a cancelled "Conference" handed over as free
///   would still pause or move the plan. An event that was transparent on Google's side (shown as free, not declined) is
///   the one to hand over as <see cref="Availability.Free"/>. A tentative or unanswered event is
///   <see cref="Availability.Busy"/>.</item>
/// </list>
///
/// <para>A calendar nobody could read (the partner's share is not done, Google would not answer) is
/// <see cref="CalendarRead.Unreadable"/>, never an empty list: the busy-time model fails closed on it. If nothing could be
/// read at all, answer with every calendar unreadable; the run then plans nothing and says so. An exception the
/// implementation cannot name is a bug and is allowed to propagate.</para>
/// </summary>
public interface IHouseholdCalendarsSource
{
    Task<HouseholdCalendarSnapshot> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation);
}

/// <summary>
/// Everything the pages have taught the plan, read once at the start of a run: the days that ended, the exercise ladders,
/// the open soreness, the assessment baselines, and the week each person's assessment was last planned for.
/// <see cref="UnreadableBecause"/> is non-null when any of those records exists and could not be read; a run then plans
/// nothing, because an unreadable ledger read as an empty one would send an established person back to the assessment
/// (and read an old soreness as healed).
/// </summary>
public sealed record WorkoutLearning(
    SkipLedger Skips,
    ProgressionLedger Progression,
    SorenessLedger Soreness,
    IReadOnlyDictionary<string, AssessmentBaselines> Baselines,
    IReadOnlyDictionary<string, DateOnly> AssessmentWeeks,
    string? UnreadableBecause)
{
    /// <summary>A first install: nothing learned, nothing unreadable.</summary>
    public static WorkoutLearning Nothing { get; } = new(
        SkipLedger.Empty,
        ProgressionLedger.Empty,
        SorenessLedger.Empty,
        new Dictionary<string, AssessmentBaselines>(),
        new Dictionary<string, DateOnly>(),
        null);

    public TrainingHistory HistoryOf(string person)
    {
        ArgumentNullException.ThrowIfNull(person);
        var assessed = AssessmentWeeks.FirstOrDefault(pair => string.Equals(pair.Key, person, StringComparison.OrdinalIgnoreCase));
        return new TrainingHistory
        {
            LastDoneDay = Skips.LastDoneDay(person),
            AssessmentWeekStart = assessed.Key is null ? null : assessed.Value,
        };
    }

    public AssessmentBaselines? BaselinesOf(string person)
    {
        ArgumentNullException.ThrowIfNull(person);
        var found = Baselines.FirstOrDefault(pair => string.Equals(pair.Key, person, StringComparison.OrdinalIgnoreCase));
        return found.Key is null ? null : found.Value;
    }
}

/// <summary>
/// Where the learned state lives (the head's files). <see cref="Load"/> never throws for a record it cannot read; it says so
/// in <see cref="WorkoutLearning.UnreadableBecause"/>. The two writes are the only ones the weekly run makes to it.
/// </summary>
public interface IWorkoutLearning
{
    WorkoutLearning Load();

    /// <summary>The week starting <paramref name="weekStart"/> has been planned as this person's assessment (<see cref="TrainingHistory.WithAssessmentWeek"/>).</summary>
    void RecordAssessmentWeek(string person, DateOnly weekStart);

    /// <summary>The doctor suggestion has gone out on a delivered page for this person (<see cref="SorenessLedger.MarkDoctorNotePrinted"/>).</summary>
    void RecordDoctorNotePrinted(string person);
}

/// <summary>
/// The morning's recovery reads from Body, by person: whoever this phone cannot read (the other adult's body lives on
/// her phone) is simply absent, and an absent person is left exactly as planned.
/// </summary>
public interface IRecoveryReadSource
{
    IReadOnlyDictionary<string, RecoveryRead> Read(IReadOnlyList<string> people, DateOnly morning);
}

/// <summary>
/// The encouragement seat for one exercise whose load has gone up since last time. It is asked only when there is news
/// (<see cref="Constitution.Domain.Voice.SeatFacts.WorkoutEncouragement"/> is null otherwise), and it never fails: a line
/// that could not be voiced comes back as the plain sentence, and null hides the seat.
/// </summary>
public interface IWorkoutEncouragement
{
    Task<string?> LineAsync(Constitution.Domain.Voice.ProgressionFacts news, DateTimeOffset now, CancellationToken cancellation);
}

/// <summary>
/// One workout document as it went to the tablet: its title, whose and when, the slot it was made for, and a fingerprint of
/// what it said (the plan-derived pages, not the voiced line). It is what lets a later run tell "already there, same",
/// "already there, changed, still before the slot" and "already there, changed, too late" apart (C-2.3.6).
/// </summary>
/// <param name="CarriesDoctorNote">The page printed the calm doctor suggestion; a replacement for the same day keeps it, since "once" means once per soreness, not once per copy.</param>
public sealed record DeliveredWorkout(
    string Name,
    string Person,
    DateOnly Day,
    SessionKind Kind,
    DateTimeOffset SlotStart,
    string Fingerprint,
    bool CarriesDoctorNote);

/// <summary>The record of what has been delivered. Pruning old entries is the implementation's business.</summary>
public interface IDeliveredWorkouts
{
    DeliveredWorkout? Find(string name);

    void Record(DeliveredWorkout delivered);

    void Remove(string name);
}

/// <summary>The weekly run's trail: one value-free phrase per outcome (see <see cref="WorkoutWeekTrail"/>).</summary>
public interface IWorkoutWeekLog
{
    void Outcome(string phrase);
}
