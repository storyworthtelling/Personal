namespace Index.Domain.Model;

/// <summary>
/// The sentence a savings goal gets on its index card (mockup 9) and in its post-it (mockup 16), as a
/// pure function so it can be tested: the projected month, "not at this pace" for a flat or falling
/// pace (D82/D83), and — once the balance is already at or past the goal — a plain "Goal reached",
/// because a date printed for a goal that is already met ("Around Oct 2026 at this pace") is a
/// projection of something that has happened.
/// </summary>
public static class SavingsGoalCopy
{
    public const string GoalReached = "Goal reached";

    /// <summary>Whether the goal has already been met. The same test <see cref="SavingsPace.ReachesIt"/> uses.</summary>
    public static bool IsReached(decimal balance, decimal goal) => goal - balance <= 0m;

    /// <summary>The card's third line.</summary>
    public static string Card(decimal balance, decimal goal, SavingsReachesIt reachesIt) =>
        IsReached(balance, goal) ? GoalReached
        : reachesIt.IsNotAtThisPace ? "Not at this pace"
        : $"Around {reachesIt.Date:MMM yyyy} at this pace";

    /// <summary>The value under "Reaches it" in the post-it.</summary>
    public static string PostIt(decimal balance, decimal goal, SavingsReachesIt reachesIt) =>
        IsReached(balance, goal) ? GoalReached
        : reachesIt.IsNotAtThisPace ? "not at this pace"
        : $"{reachesIt.Date:MMM yyyy}";
}
