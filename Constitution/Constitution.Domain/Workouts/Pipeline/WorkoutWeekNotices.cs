using Constitution.Domain.Notices;

namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>
/// What a weekly run says on the Waiting page: one Problem notice, <see cref="NoticeKind.WeekNotPlanned"/>, while the week cannot be
/// planned, filed under one cause so a second failed run changes nothing (and keeps the date the trouble began), and taken away by the
/// first run that plans the week. Why it could not be planned (a calendar not set up, not shared, would not answer; a record unreadable) is
/// the Diagnostics trail's to say, in a fixed phrase: the page names no calendar, person or file.
///
/// <para>Delivery to the tablet has its own notice (<see cref="NoticeKind.ReMarkableDelivery"/>, raised by the delivery service), which
/// this never touches: a planned week whose page the tablet refused is a planned week.</para>
/// </summary>
public static class WorkoutWeekNotices
{
    /// <summary>The cause the notice is filed under. Frozen: the board is in memory, but the launch restore files it under the same word.</summary>
    public const string NotPlannedCause = "week:not-planned";

    /// <summary>States what a run found. Returns whether the board changed.</summary>
    public static bool Apply(NoticeBoard board, WorkoutWeekOutcome outcome, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome switch
        {
            WorkoutWeekOutcome.NotPlanned => board.Raise(NoticeKind.WeekNotPlanned, NotPlannedCause, now),
            WorkoutWeekOutcome.Planned => board.Resolve(NotPlannedCause),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "This kind of outcome has no notice; add one here."),
        };
    }
}
