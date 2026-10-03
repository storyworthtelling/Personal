namespace Index.Domain.Model;

/// <summary>One category whose limit could not be read off the worksheet (D49).</summary>
public sealed record UnreadableLimit(string CategoryName);

/// <summary>
/// D49's dog-ear notices for Budget, on the same shared <see cref="AccountNotice"/>/
/// <see cref="NoticeSeverity"/> types Accounts' own <see cref="AccountNotices"/> uses — pure and
/// stateless for the same reason (Guard G25's rule applies here too, unnamed in the guard ledger only
/// because Release B wrote it before Budget existed): recomputed from live category state every call,
/// so a category whose limit gets read on a later worksheet pass simply stops producing a notice, with
/// nothing to mark "seen".
/// </summary>
public static class BudgetNotices
{
    public static IReadOnlyList<AccountNotice> Build(IReadOnlyList<UnreadableLimit> unreadableLimits)
    {
        var notices = new List<AccountNotice>();
        foreach (var limit in unreadableLimits)
        {
            notices.Add(new AccountNotice(
                $"{limit.CategoryName}'s limit isn't read yet",
                "Sent back to the reMarkable to check.",
                // D49's mockup draws this fold in Snowball's own in-progress teal, "because the budget
                // is waiting, not wrong" — the Progress role the shared DogEarHue gained for it.
                NoticeSeverity.Progress,
                ActionLabel: null));
        }
        return notices;
    }
}
