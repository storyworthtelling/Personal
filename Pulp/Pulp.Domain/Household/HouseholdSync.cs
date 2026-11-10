using Storyworthtelling.Time;
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

    /// <summary>Google turned this phone's sign-in away (audit 11 §20): edits wait on the phone until it signs in again.</summary>
    SignInNeeded,

    /// <summary>The pass failed for a reason that is neither the network nor the sign-in; it is tried again later.</summary>
    Retrying,
}

/// <summary>
/// What a household screen may do on a phone, by whether it owns the file (D166). Both adults manage the
/// children and the phone's own viewer; the services and adding or removing people stay the owner's. A
/// role nobody has known (<see cref="HouseholdFileRole.Unknown"/>) is not restricted: it is a phone with no
/// vault at all, which must be able to set its own household up and has nobody else to protect it from.
/// The window between a cold start and the vault's first read is closed elsewhere — the vault is read at
/// launch and the last role is remembered per account (<see cref="HouseholdRoleMemory"/>) — so a phone
/// the household was shared to does not pass through this arm.
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
    /// <param name="zone">The viewer's zone, which decides what day an old sync falls on; the phone's own when omitted.</param>
    public static string Line(HouseholdSyncStatus status, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        return status.State switch
        {
            HouseholdSyncState.Synced when status.LastSynced is { } at => "Synced with Drive " + Ago(at, now, zone ?? TimeZoneInfo.Local) + ".",
            HouseholdSyncState.Synced => "Synced with Drive.",
            HouseholdSyncState.Offline => "Drive couldn't be reached. Changes are kept on this phone and go up when it can.",
            HouseholdSyncState.WaitingForShare => "Waiting for the owner to share the household with this account.",
            HouseholdSyncState.SignInNeeded => "Google turned this phone's sign-in away. Changes are kept on this phone; sign in again in Settings.",
            HouseholdSyncState.Retrying when !string.IsNullOrWhiteSpace(status.Reason) => status.Reason,
            HouseholdSyncState.Retrying => "The household didn't sync this time. Changes are kept on this phone, and Pulp tries again later.",
            _ => status.Reason,
        };
    }

    // The suite's relative words (DESIGN.md §11), read in the viewer's zone: an instant just after midnight UTC
    // is still the day before for most of the world.
    // A weekday or a date takes "on", so the line still reads as a sentence: "Synced with Drive on Friday."
    private static string Ago(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        var words = RelativeTime.Format(at, now, zone);
        return words is "just now" or "yesterday" || words.EndsWith(" ago", StringComparison.Ordinal) ? words : "on " + words;
    }
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

    /// <summary>The same phone, its Google sign-in refused; when it last landed is kept.</summary>
    public HouseholdSyncStatus SignInRefused() => new(HouseholdSyncState.SignInNeeded, LastSynced, string.Empty);

    /// <summary>The same phone, a pass that failed for another reason; <paramref name="reason"/> is a sentence, never an exception's message.</summary>
    public HouseholdSyncStatus PassFailed(string? reason = null) => new(HouseholdSyncState.Retrying, LastSynced, reason ?? string.Empty);

    /// <summary>The dog-ear notice this status raises, if any.</summary>
    public WaitingNotice? Notice(DateTimeOffset at) => State != HouseholdSyncState.Unreadable
        ? null
        : UnreadableReason == HouseholdUnreadableReason.NewerFormat
            ? WaitingNotices.HouseholdFromNewerVersion(at)
            : WaitingNotices.HouseholdUnreadable(at);
}

/// <summary>
/// Pulp's one piece of the sync policy: whether a pass may run at all, and the status that says why not.
/// The rest of the policy (what a pass adopts, when it writes, who may create the file) is the shared
/// engine's <see cref="Storyworthtelling.Household.HouseholdSync"/>.
/// </summary>
public static class HouseholdSync
{
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
}
