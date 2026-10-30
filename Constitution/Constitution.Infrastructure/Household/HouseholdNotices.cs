using Constitution.Domain.Notices;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Household;

/// <summary>
/// Turns the household file's standing into the notice waiting behind the dog-ear (D109), and takes it away when the cause goes.
/// Pure: the head states each settled pass here. Only a file that will not open raises; only a pass that read and published it
/// (<see cref="HouseholdFileState.Synced"/>) resolves. Offline, local-only and waiting-for-share say nothing about whether the file
/// opens, so they leave it as it was. One notice at a time: a changed reason replaces the old one.
/// </summary>
public static class HouseholdNotices
{
    /// <summary>The key the board files the notice under for this reason; it names the reason only.</summary>
    public static string CauseKey(HouseholdUnreadableReason reason) => HouseholdNoticeCause.Prefix + Word(reason);

    /// <summary>States one standing to the board. Returns whether the board changed.</summary>
    public static bool Apply(NoticeBoard board, HouseholdStanding standing, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(standing);

        switch (standing.State)
        {
            case HouseholdFileState.Unreadable:
                var key = CauseKey(standing.Reason ?? HouseholdUnreadableReason.Damaged);
                var changed = ResolveOthers(board, key);
                return board.Raise(NoticeKind.HouseholdFileUnreadable, key, now) || changed;
            case HouseholdFileState.Synced:
                return ResolveOthers(board, keep: null);
            default:
                return false;
        }
    }

    private static bool ResolveOthers(NoticeBoard board, string? keep)
    {
        var changed = false;
        foreach (var notice in board.Waiting().ToList())
        {
            if (notice.Kind == NoticeKind.HouseholdFileUnreadable && notice.CauseKey != keep)
            {
                changed |= board.Resolve(notice.CauseKey);
            }
        }

        return changed;
    }

    private static string Word(HouseholdUnreadableReason reason)
    {
        switch (reason)
        {
            case HouseholdUnreadableReason.NoPassphrase:
                return HouseholdNoticeCause.MissingKeyWord;
            case HouseholdUnreadableReason.WrongPassphrase:
                return HouseholdNoticeCause.MismatchWord;
            case HouseholdUnreadableReason.Damaged:
                return HouseholdNoticeCause.DamagedWord;
            case HouseholdUnreadableReason.NewerFormat:
                return HouseholdNoticeCause.NewerWord;
            default:
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "This reason has no notice word; add one here.");
        }
    }
}
