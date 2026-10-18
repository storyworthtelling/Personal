namespace Constitution.Domain.Workouts;

/// <summary>
/// How many sessions a person is asked for in a week, before the calendar has its say. The owner is
/// "mostly inactive, starting fresh", and a plan that is too hard is the named risk, so the number
/// starts low and is earned: three a week, two after a week with two or more skipped, four only after a
/// full week of three done and none skipped. Sundays are extra and gentle (see <see cref="WorkoutPlanner"/>)
/// and are not counted here.
/// </summary>
public static class WorkoutFrequency
{
    public const int Base = 3;

    public const int Eased = 2;

    public const int Earned = 4;

    /// <summary>Skips in a week that make the next week easier.</summary>
    public const int SkipsThatEase = 2;

    public static int Target(RecoveryInputs recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        if (recovery.SessionsSkippedLastWeek >= SkipsThatEase)
        {
            return Eased;
        }

        return recovery.SessionsDoneLastWeek >= Base && recovery.SessionsSkippedLastWeek == 0 ? Earned : Base;
    }

    /// <summary>
    /// The kinds a week of <paramref name="target"/> sessions is made of, in the order they are filled: strength, then cardio
    /// (<see cref="SessionKind.OutdoorCardio"/> here, turned to a treadmill by the planner when one is ticked and it is that
    /// kind of week), then mobility, then a second strength.
    /// </summary>
    public static IReadOnlyList<SessionKind> Menu(int target)
    {
        if (target < 1 || target > Earned)
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, $"A week asks for 1 to {Earned} sessions.");
        }

        SessionKind[] all = [SessionKind.Strength, SessionKind.OutdoorCardio, SessionKind.Mobility, SessionKind.Strength];
        return all.Take(target).ToList();
    }
}
