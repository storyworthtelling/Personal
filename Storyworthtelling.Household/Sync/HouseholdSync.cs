namespace Storyworthtelling.Household;

/// <summary>Why a household file in Drive could not be opened.</summary>
public enum HouseholdUnreadableReason
{
    NoPassphrase,
    WrongPassphrase,
    Damaged,
    NewerFormat,
}

/// <summary>Whether this install's account owns the household file or has it shared to it (finding §2.7).</summary>
public enum HouseholdFileRole
{
    /// <summary>
    /// Not known yet: the vault has not been read and no earlier read is remembered for this account
    /// (<see cref="HouseholdRoleMemory"/>). Nothing is created in Drive, but permissions are the owner's.
    /// </summary>
    Unknown,

    /// <summary>The owner's account: the file lives in its Drive, so it may create it.</summary>
    Owner,

    /// <summary>The other adult's account: it edits the file once the owner shares it, and never creates one.</summary>
    SharedWith,
}

/// <summary>How one publish pass ended.</summary>
public enum HouseholdPublishOutcome
{
    /// <summary>Drive already held everything this install has; nothing was written.</summary>
    AlreadyCurrent,

    /// <summary>The merge was written and the re-read shows it landed.</summary>
    Written,

    /// <summary>Lost the race even on the second write; the install keeps the merge and the next pass carries it.</summary>
    LeftForNextPass,

    /// <summary>No file is visible and this install may not create one.</summary>
    NotYetShared,
}

/// <summary>
/// The sync policy (finding §2.7). The install's own file is the working copy every screen reads and
/// every edit lands in first; Drive is where the installs meet. A pass pulls Drive, merges, pushes
/// only if Drive is missing something, re-reads and follows <see cref="HouseholdPublish"/>. Nothing a
/// pass does can lose a local edit: what it brings home is <see cref="Adopt">merged</see> into the
/// install's file as it stands when the pass returns, not written over it.
/// </summary>
public static class HouseholdSync
{
    /// <summary>
    /// What the install keeps after a pass: its file as it stands now — including edits made while the
    /// pass was on the network — merged with what the pass settled on. On an install whose file is
    /// empty, that is simply what Drive holds: the first run adopts the household.
    /// </summary>
    public static HouseholdDocument Adopt(HouseholdDocument localNow, HouseholdDocument published) =>
        HouseholdMerge.Merge(localNow, published);

    /// <summary>True when the merge holds something the file in Drive does not: only then is anything written.</summary>
    public static bool NeedsWrite(HouseholdDocument merged, HouseholdDocument read)
    {
        ArgumentNullException.ThrowIfNull(merged);
        ArgumentNullException.ThrowIfNull(read);
        return !merged.ContentEquals(read);
    }

    /// <summary>Only the owner's account creates the file; any other would make a second household nobody merges with.</summary>
    public static bool MayCreateFile(HouseholdFileRole role) => role == HouseholdFileRole.Owner;
}
