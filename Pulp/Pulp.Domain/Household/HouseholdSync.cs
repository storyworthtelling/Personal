using Pulp.Domain.Actions;

namespace Pulp.Domain.Household;

/// <summary>Where this phone's household stands against the shared file in Drive.</summary>
public enum HouseholdSyncState
{
    /// <summary>No passphrase or no Google account: the phone works from its own file and never reaches Drive.</summary>
    LocalOnly,

    /// <summary>The last pass landed: Drive and this phone hold the same household.</summary>
    Synced,

    /// <summary>Drive could not be reached. Edits are kept on the phone and the next pass carries them.</summary>
    Offline,

    /// <summary>Drive holds a household file this phone cannot open. It is never written over (G14).</summary>
    Unreadable,

    /// <summary>The file is not visible to this account yet — the owner has not shared it. Nothing is created in its place.</summary>
    WaitingForShare,
}

/// <summary>Why a household file in Drive could not be opened.</summary>
public enum HouseholdUnreadableReason
{
    NoPassphrase,
    WrongPassphrase,
    Damaged,
    NewerFormat,
}

/// <summary>Whether this phone's account owns the household file or has it shared to it (finding §2.7).</summary>
public enum HouseholdFileRole
{
    /// <summary>Not known yet (the vault has not been read). Treated as not the owner: nothing is created.</summary>
    Unknown,

    /// <summary>The owner's account: the file lives in its Drive, so it may create it.</summary>
    Owner,

    /// <summary>The other adult's account: it edits the file once the owner shares it, and never creates one.</summary>
    SharedWith,
}

/// <summary>
/// What a household screen may do on a phone, by whether it owns the file (D166). Both adults manage the
/// children and the phone's own viewer; the services and adding or removing people stay the owner's. A
/// role not known yet is not restricted: it is the owner's phone before the vault was read, or a phone
/// with no vault at all, where there is nobody else to protect the household from.
/// </summary>
public sealed record HouseholdPermissions(
    bool CanEditServices, bool CanAddOrRemoveViewers, bool CanEditChildren, bool CanRenameOwnViewer, string? Note)
{
    public const string OwnerOnlyNote = "Services, and adding or removing people, are the owner's to change, on their phone.";

    public static HouseholdPermissions For(HouseholdFileRole role) => role == HouseholdFileRole.SharedWith
        ? new HouseholdPermissions(false, false, true, true, OwnerOnlyNote)
        : new HouseholdPermissions(true, true, true, true, null);
}

/// <summary>When a screen showing the household reads it again: a pass brought records home, or the status moved.</summary>
public static class SyncRefresh
{
    public static bool Needed(HouseholdSyncStatus? previous, HouseholdSyncStatus next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return previous is null || next.BroughtRecords || previous.State != next.State;
    }
}

/// <summary>The quiet line a household screen carries about the sync. Plain, never voiced, never a raw error.</summary>
public static class HouseholdSyncText
{
    public static string Line(HouseholdSyncStatus status, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(status);
        return status.State switch
        {
            HouseholdSyncState.Synced when status.LastSynced is { } at => "Synced with Drive " + Ago(at, now) + ".",
            HouseholdSyncState.Synced => "Synced with Drive.",
            HouseholdSyncState.Offline => "Drive couldn't be reached. Changes are kept on this phone and go up when it can.",
            HouseholdSyncState.WaitingForShare => "Waiting for the owner to share the household with this account.",
            _ => status.Reason,
        };
    }

    private static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes} min ago";
        }

        return elapsed < TimeSpan.FromDays(1)
            ? $"{(int)elapsed.TotalHours} h ago"
            : "on " + at.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>How one publish pass ended.</summary>
public enum HouseholdPublishOutcome
{
    /// <summary>Drive already held everything this phone has; nothing was written.</summary>
    AlreadyCurrent,

    /// <summary>The merge was written and the re-read shows it landed.</summary>
    Written,

    /// <summary>Lost the race even on the second write; the phone keeps the merge and the next pass carries it.</summary>
    LeftForNextPass,

    /// <summary>No file is visible and this phone may not create one.</summary>
    NotYetShared,
}

/// <summary>
/// What the screens are told about the household's sync: a state, when it last landed, and the reason
/// when it is not landing. A notice is raised only for an unreadable file — being offline or local-only
/// is a fact the Settings page states, not a problem waiting behind the dog-ear.
/// </summary>
public sealed record HouseholdSyncStatus(
    HouseholdSyncState State, DateTimeOffset? LastSynced, string Reason, HouseholdUnreadableReason? UnreadableReason = null, bool BroughtRecords = false)
{
    public static HouseholdSyncStatus LocalOnly(string reason) => new(HouseholdSyncState.LocalOnly, null, reason);

    public static HouseholdSyncStatus Synced(DateTimeOffset at) => new(HouseholdSyncState.Synced, at, string.Empty);

    public static HouseholdSyncStatus Offline(DateTimeOffset? lastSynced, string reason) => new(HouseholdSyncState.Offline, lastSynced, reason);

    public static HouseholdSyncStatus WaitingForShare(DateTimeOffset? lastSynced, string reason) =>
        new(HouseholdSyncState.WaitingForShare, lastSynced, reason);

    public static HouseholdSyncStatus Unreadable(HouseholdUnreadableReason why, string reason, DateTimeOffset? lastSynced) =>
        new(HouseholdSyncState.Unreadable, lastSynced, reason, why);

    /// <summary>The same phone, now unable to reach Drive; when it last landed is kept.</summary>
    public HouseholdSyncStatus WentOffline(string reason) => Offline(LastSynced, reason);

    /// <summary>The dog-ear notice this status raises, if any.</summary>
    public WaitingNotice? Notice(DateTimeOffset at) => State != HouseholdSyncState.Unreadable
        ? null
        : UnreadableReason == HouseholdUnreadableReason.NewerFormat
            ? WaitingNotices.HouseholdFromNewerVersion(at)
            : WaitingNotices.HouseholdUnreadable(at);
}

/// <summary>
/// The sync policy (finding §2.7). The phone's own file is the working copy every screen reads and
/// every edit lands in first; Drive is where the two phones meet. A pass pulls Drive, merges, pushes
/// only if Drive is missing something, re-reads and follows <see cref="HouseholdPublish"/>. Nothing a
/// pass does can lose a local edit: what it brings home is <see cref="Adopt">merged</see> into the
/// phone's file as it stands when the pass returns, not written over it.
/// </summary>
public static class HouseholdSync
{
    /// <summary>
    /// What the phone keeps after a pass: its file as it stands now — including edits made while the
    /// pass was on the network — merged with what the pass settled on. On a phone whose file is empty,
    /// that is simply what Drive holds: the first run adopts the household.
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

    /// <summary>Null when a pass may run; otherwise the local-only status saying what is missing.</summary>
    public static HouseholdSyncStatus? Readiness(bool hasPassphrase, bool hasAccount)
    {
        if (!hasPassphrase)
        {
            return HouseholdSyncStatus.LocalOnly("There is no vault passphrase on this phone, so the household stays on it until one is entered under Vault.");
        }

        return hasAccount
            ? null
            : HouseholdSyncStatus.LocalOnly("No Google account is signed in, so the household stays on this phone until one is.");
    }

    /// <summary>Only the owner's account creates the file; any other would make a second household nobody merges with.</summary>
    public static bool MayCreateFile(HouseholdFileRole role) => role == HouseholdFileRole.Owner;
}

/// <summary>
/// When the next pass is due, with time passed in. Opening the app is due at once; an edit waits for
/// <see cref="Debounce"/> of quiet, so a run of ticks is one pass; a Drive that could not be reached is
/// retried with doubling backoff up to <see cref="LongestRetry"/>, and an edit does not bring that
/// forward; an unreadable file stops passes until someone asks for one (a new passphrase, Sync now).
/// Not thread-safe: the caller holds its own lock.
/// </summary>
public sealed class HouseholdSyncSchedule
{
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan LongestRetry = TimeSpan.FromHours(1);

    private int failures;
    private DateTimeOffset? retryAt;
    private bool blocked;

    /// <summary>When the next pass is due; null when nothing is waiting.</summary>
    public DateTimeOffset? NextDue { get; private set; }

    public void Opened(DateTimeOffset now) => Due(now);

    public void Edited(DateTimeOffset now)
    {
        if (blocked)
        {
            return;
        }

        var debounced = now + Debounce;
        NextDue = retryAt is { } retry && retry > debounced ? retry : debounced;
    }

    /// <summary>A pass begins; anything due is now its job. An edit after this schedules another.</summary>
    public void Started(DateTimeOffset now) => NextDue = null;

    public void Succeeded()
    {
        failures = 0;
        retryAt = null;
    }

    public void Failed(DateTimeOffset now)
    {
        failures++;
        var wait = TimeSpan.FromTicks(FirstRetry.Ticks * (1L << Math.Min(failures - 1, 16)));
        retryAt = now + (wait > LongestRetry ? LongestRetry : wait);
        NextDue = retryAt;
    }

    /// <summary>The file in Drive cannot be opened: no more passes until <see cref="Asked"/>.</summary>
    public void Blocked()
    {
        blocked = true;
        NextDue = null;
    }

    /// <summary>Someone asked for a pass (Sync now, a new passphrase): due at once, whatever was holding it.</summary>
    public void Asked(DateTimeOffset now)
    {
        blocked = false;
        failures = 0;
        retryAt = null;
        NextDue = now;
    }

    public bool IsDue(DateTimeOffset now) => NextDue is { } due && due <= now;

    private void Due(DateTimeOffset now)
    {
        if (!blocked)
        {
            NextDue = now;
        }
    }
}
