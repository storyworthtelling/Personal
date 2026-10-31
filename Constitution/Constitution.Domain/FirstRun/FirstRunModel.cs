namespace Constitution.Domain.FirstRun;

/// <summary>The three things a fresh install asks for, and nothing else (C-8.3.1).</summary>
public enum FirstRunStep
{
    GoogleSignIn,
    VaultPassphrase,
    Equipment,
}

/// <summary>
/// What is already true of this install. Each fact may have been satisfied by the owner just now or
/// restored from the vault, so the model does not care which.
/// </summary>
public sealed record FirstRunState(bool SignedIn, bool HasPassphrase, bool EquipmentConfirmed);

/// <summary>
/// First run as a pure decision: three steps in order, a step skipped once it is satisfied. Health
/// Connect, Sleep Number and the reMarkable are deliberately not steps; they are asked when first
/// needed or restored from the vault.
/// </summary>
public static class FirstRunModel
{
    public static IReadOnlyList<FirstRunStep> Steps { get; } =
        [FirstRunStep.GoogleSignIn, FirstRunStep.VaultPassphrase, FirstRunStep.Equipment];

    /// <summary>The first step still owed, or null when all three are satisfied.</summary>
    public static FirstRunStep? Current(FirstRunState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Steps.Cast<FirstRunStep?>().FirstOrDefault(step => !Satisfied(step!.Value, state));
    }

    public static bool IsDone(FirstRunState state) => Current(state) is null;

    // Indexed by the step's position in Steps (the enum's declaration order).
    private static readonly string[] Sentences =
    [
        "Sign in with Google so Constitution can reach your calendar and Drive.",
        "Choose the vault passphrase. It unlocks the saved connections.",
        "Tick what you have. The plan only uses these.",
    ];

    private static readonly string[] Labels = ["Sign in with Google", "Save passphrase", "Done"];

    /// <summary>The status sentence shown for a step.</summary>
    public static string Sentence(FirstRunStep step) => Sentences[Index(step)];

    /// <summary>The primary action's label in the pinned bar.</summary>
    public static string ActionLabel(FirstRunStep step) => Labels[Index(step)];

    private static int Index(FirstRunStep step) =>
        Enum.IsDefined(step)
            ? (int)step
            : throw new ArgumentOutOfRangeException(nameof(step), step, "That is not a first-run step.");

    private static bool Satisfied(FirstRunStep step, FirstRunState state) => step switch
    {
        FirstRunStep.GoogleSignIn => state.SignedIn,
        FirstRunStep.VaultPassphrase => state.HasPassphrase,
        _ => state.EquipmentConfirmed,
    };
}
