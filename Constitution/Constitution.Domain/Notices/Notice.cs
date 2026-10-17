using Codex.Domain.Design;

namespace Constitution.Domain.Notices;

/// <summary>
/// One thing waiting behind the fold. <paramref name="CauseKey"/> is what resolves it: the notice
/// stays exactly as long as whatever raised it, and <see cref="NoticeBoard.Resolve"/> names that
/// cause. <paramref name="CreatedAt"/> is when the cause began, which is what the Waiting page orders
/// newest-first by and prints as a relative date.
/// </summary>
public sealed record Notice(NoticeKind Kind, string CauseKey, DateTimeOffset CreatedAt)
{
    /// <summary>The role this notice wears.</summary>
    public FoldRole Role => NoticeKinds.RoleOf(Kind);
}

/// <summary>
/// What the corner shows: the role it wears, whether it is an undo (the back-turn arrow), the kind of
/// notice it is wearing (null for an undo), and when it lapses (null for a notice, which waits with no
/// clock running).
/// </summary>
public sealed record NoticeFold(FoldRole Role, bool IsUndo, NoticeKind? Kind, DateTimeOffset? Expires);
