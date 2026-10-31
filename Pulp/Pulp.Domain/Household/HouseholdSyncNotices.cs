namespace Pulp.Domain.Household;

/// <summary>What the shell does with the dog-ear when the sync status moves: raise the notice, or withdraw whatever the sync raised before.</summary>
public sealed record SyncNoticeChange(Actions.WaitingNotice? Raise, string Key);

/// <summary>Keyed by cause, so a notice leaves when the cause does (mockup 12).</summary>
public static class HouseholdSyncNotices
{
    public static SyncNoticeChange For(HouseholdSyncStatus status, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new SyncNoticeChange(status.Notice(at), Actions.WaitingNotices.HouseholdUnreadable(at).Key);
    }
}
