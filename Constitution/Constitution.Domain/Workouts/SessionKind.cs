namespace Constitution.Domain.Workouts;

/// <summary>
/// What a session is. The five the planner may place: three kinds of effort a beginner can do
/// (<see cref="Strength"/>, <see cref="OutdoorCardio"/>, <see cref="Treadmill"/>), and two gentle ones
/// (<see cref="Mobility"/>, <see cref="GentleWalk"/>). A family walk is a <see cref="GentleWalk"/>
/// that two people do together, not a sixth kind.
/// </summary>
public enum SessionKind
{
    Strength,

    OutdoorCardio,

    Treadmill,

    Mobility,

    GentleWalk,
}

public static class SessionKinds
{
    /// <summary>
    /// Whether the session asks the body for recovery afterwards. Strength and cardio do; mobility and
    /// a gentle walk do not, which is why they may follow a hard day and a hard day may not follow a hard day.
    /// </summary>
    public static bool IsHard(SessionKind kind) => kind is SessionKind.Strength or SessionKind.OutdoorCardio or SessionKind.Treadmill;
}
