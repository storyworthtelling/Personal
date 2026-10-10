using Pulp.Domain.People;

namespace Pulp.Domain.FirstRun;

/// <summary>The steps of first run (D142), in the order they are asked.</summary>
public enum FirstRunStep
{
    /// <summary>The Google account and the vault passphrase, which restore the keys by themselves.</summary>
    Vault,

    /// <summary>The people in the household, when Drive does not already hold them.</summary>
    Household,

    /// <summary>Which adult this phone belongs to (D97).</summary>
    ThisPhone,

    Done,
}

/// <summary>
/// What first run needs to know to say what comes next, read fresh from the stores each time — which
/// is what makes it resumable: nothing remembers "the step we were on" that could disagree with what
/// is actually in place. <see cref="Skipped"/> and <see cref="Finished"/> are the only things first
/// run keeps of its own.
/// </summary>
public sealed record FirstRunState(
    bool SignedIn,
    bool HasPassphrase,
    int KeysPresent,
    bool HasRoster,
    ThisPhoneStatus Phone,
    IReadOnlySet<FirstRunStep> Skipped,
    bool Finished);

/// <summary>
/// Which step of first run comes next (D142: enter the passphrase; if the household file exists
/// everything appears; otherwise the people; no tour). A step is passed when what it sets up is in
/// place or the owner skipped it; the first one that is neither is next.
/// </summary>
public static class FirstRunPlan
{
    /// <summary>Pulp's household keys: Streaming Availability, the rating source and the trailer source.</summary>
    public const int KeyCount = 3;

    public static FirstRunStep Next(FirstRunState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!VaultInPlace(state) && !state.Skipped.Contains(FirstRunStep.Vault))
        {
            return FirstRunStep.Vault;
        }

        if (!state.HasRoster && !state.Skipped.Contains(FirstRunStep.Household))
        {
            return FirstRunStep.Household;
        }

        // With no roster there is nobody to match the phone to, so the last step has nothing to ask.
        if (state.HasRoster && state.Phone != ThisPhoneStatus.Known && !state.Skipped.Contains(FirstRunStep.ThisPhone))
        {
            return FirstRunStep.ThisPhone;
        }

        return FirstRunStep.Done;
    }

    /// <summary>
    /// Every step can be skipped: without the vault the clients take their missing-key path, without a
    /// roster nothing is filtered for a child because no child is named, and an unmatched phone shows
    /// <see cref="ThisPhone.UnmatchedSentence"/> rather than anyone's private lists. Each is set later
    /// in Settings.
    /// </summary>
    public static bool CanSkip(FirstRunStep step) => step != FirstRunStep.Done;

    private static bool VaultInPlace(FirstRunState state) =>
        state.SignedIn && state.HasPassphrase && state.KeysPresent >= KeyCount;
}

/// <summary>Whether the shell opens first run at launch.</summary>
public static class FirstRunGate
{
    /// <summary>
    /// True until first run has been finished once (D142: no tour), and only while something it sets
    /// up is neither in place nor skipped — so a half-done first run resumes at the next launch.
    /// </summary>
    public static bool ShouldOpen(FirstRunState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        // At launch the keys are in memory only once the vault has been read, so a phone holding its
        // passphrase and account would look keyless until that refresh lands. Counting them here would
        // reopen first run on every launch; a missing key is the Vault page's to say.
        return !state.Finished && FirstRunPlan.Next(state with { KeysPresent = FirstRunPlan.KeyCount }) != FirstRunStep.Done;
    }
}
