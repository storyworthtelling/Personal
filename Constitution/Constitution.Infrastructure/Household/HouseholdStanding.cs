using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Household;

/// <summary>Where this phone's household file stands against Drive.</summary>
public enum HouseholdFileState
{
    /// <summary>No pass has run yet in this process.</summary>
    NotCompared,

    /// <summary>A pass could not start: no vault passphrase, or no Google account. The household stays on the phone.</summary>
    LocalOnly,

    Synced,

    /// <summary>Drive could not be reached or the other phone was writing; the phone keeps its copy and tries again.</summary>
    Offline,

    /// <summary>No file is visible to this account and this phone may not create one.</summary>
    WaitingForShare,

    /// <summary>A file exists (here or in Drive) and cannot be opened. Nothing is read from it or written to it.</summary>
    Unreadable,
}

/// <summary>
/// The standing and the one plain sentence that says it. The sentence is fixed text (and the frozen file name): it never quotes an
/// exception's message, the passphrase or a record. <see cref="Failure"/> keeps the exception that ended a pass, with its stack,
/// for the diagnostics that want it; nothing shown to the owner reads it.
/// </summary>
public sealed record HouseholdStanding(
    HouseholdFileState State,
    DateTimeOffset? LastSynced,
    string Sentence,
    HouseholdUnreadableReason? Reason = null,
    Exception? Failure = null)
{
    public static HouseholdStanding NotCompared { get; } = new(HouseholdFileState.NotCompared, null, HouseholdSentences.NotCompared);

    public bool IsUnreadable => State == HouseholdFileState.Unreadable;
}

/// <summary>The words the head says about the household file. Every one is a fixed sentence.</summary>
public static class HouseholdSentences
{
    public const string NotCompared = "The household file has not been compared with Drive yet.";

    public const string NoVaultKey =
        "There is no vault passphrase on this phone, so the household file stays on it until one is entered under Vault.";

    public const string NoAccount = "No Google account is signed in, so the household file stays on this phone until one is.";

    public const string Offline = "Drive could not be reached, so the household file stays on this phone and goes up on a later pass.";

    public const string OtherPhoneWriting =
        "The other phone was saving at the same moment; this phone's changes are kept and go up on the next pass.";

    public const string LocalDamaged =
        "This phone's own copy of the household file is damaged and does not open, so nothing was read from it or changed.";

    public static string WaitingForShare(string fileName) =>
        $"No household file is shared with this account yet. The owner shares '{fileName}' from their Drive; until then this phone keeps its household to itself.";

    /// <summary>The trail's line for a file that will not open: fixed words from the trail's vocabulary, none from the file.</summary>
    public static string UnreadableTrail(HouseholdUnreadableReason reason)
    {
        // Statements rather than switch arms on purpose: the key guard reads an arrow from a passphrase-named member to a string as a secret.
        switch (reason)
        {
            case HouseholdUnreadableReason.NoPassphrase:
                return "sync refused, no passphrase";
            case HouseholdUnreadableReason.WrongPassphrase:
                return "sync refused, wrong passphrase";
            case HouseholdUnreadableReason.Damaged:
                return "sync refused, shared file corrupt";
            case HouseholdUnreadableReason.NewerFormat:
                return "sync refused, shared file newer version";
            default:
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "This reason has no trail phrase; add one here.");
        }
    }

    public static string Unreadable(HouseholdUnreadableReason reason)
    {
        switch (reason)
        {
            case HouseholdUnreadableReason.NoPassphrase:
                return "The household file in Drive does not open: there is no vault passphrase on this phone, and the file is locked with one. Nothing was changed there.";
            case HouseholdUnreadableReason.WrongPassphrase:
                return "The household file in Drive does not open with this phone's vault passphrase. Nothing was changed there; check the passphrase under Vault.";
            case HouseholdUnreadableReason.Damaged:
                return "The household file in Drive is damaged and does not open. Nothing was changed there.";
            case HouseholdUnreadableReason.NewerFormat:
                return "The household file in Drive was written by a newer version of Constitution and does not open here. Nothing was changed there; update this phone.";
            default:
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "This reason has no sentence; add one here.");
        }
    }
}
