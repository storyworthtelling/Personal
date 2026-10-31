namespace Constitution.Domain.Notices;

/// <summary>One ordinary notification Constitution posts (C-9.1.3, D83): fixed words, no number, and the bookmark role it carries as an extra (PC1).</summary>
public sealed record NoticeNotification(NoticeKind Kind, int Id, string RoleWire, string Title, string Text);

/// <summary>What to do to the shade for the notices now waiting: post these, cancel those.</summary>
public sealed record NoticeNotificationPlan(IReadOnlyList<NoticeNotification> Post, IReadOnlyList<NoticeKind> Cancel);

/// <summary>
/// The brief's short list as ordinary Android notifications (C-9.1.3; D83, D91; PC1, PC2): a scan waiting,
/// the box under ten, and the three errors (reMarkable delivery, Health Connect revoked, the bed offline).
/// The same path serves both phones. On the partner's it is an ordinary notification; on the owner's locked
/// phone the lockdown suppresses the alert, so it shows only as Flyleaf's bookmark, coloured by the role
/// extra each one carries.
///
/// <para><b>The role on the wire is the bookmark's, not the fold's.</b> A waiting scan is Progress teal on the
/// dog-ear but Reference slate on the bookmark (D91, as page 19 draws it), so the wire says <c>reference</c>.</para>
///
/// <para><b>No number is ever carried</b> (C-7.4.3): the words are fixed here, in one place, and a test reads
/// every one for a digit. The wire strings are frozen literals; Flyleaf has the same two on its side and a
/// content-read guard pins them equal.</para>
/// </summary>
public static class NoticeNotifications
{
    /// <summary>The Intent extra the role travels in. Frozen; Flyleaf reads this exact key.</summary>
    public const string RoleExtraKey = "com.storyworthtelling.constitution.NOTICE_ROLE";

    /// <summary>The three errors: bookmark rose.</summary>
    public const string ProblemRole = "problem";

    /// <summary>A scan waiting and the box under ten: bookmark slate (D91).</summary>
    public const string ReferenceRole = "reference";

    /// <summary>The one notification channel Constitution posts on.</summary>
    public const string ChannelId = "constitution-notices";

    private static readonly NoticeNotification[] Table =
    [
        new(NoticeKind.ReMarkableDelivery, 4101, ProblemRole, "Pages did not reach the tablet", "Open Constitution to see what to do."),
        new(NoticeKind.HealthConnectRevoked, 4102, ProblemRole, "Health Connect was turned off", "The charts have stopped. Open Constitution to turn it back on."),
        new(NoticeKind.BedOffline, 4103, ProblemRole, "The bed cannot be reached", "Open Constitution to see what to do."),
        new(NoticeKind.ScanToCheck, 4104, ReferenceRole, "A scanned page is waiting", "It needs a quick look before it is kept."),
        new(NoticeKind.BoxUnderTen, 4105, ReferenceRole, "The budget box is running low", "Worth knowing. Nothing to do."),
    ];

    /// <summary>The kinds that post, in table order.</summary>
    public static IReadOnlyList<NoticeKind> ShortList { get; } = Table.Select(note => note.Kind).ToList();

    /// <summary>The notification a kind posts, or null for a kind that never does.</summary>
    public static NoticeNotification? For(NoticeKind kind) => Table.FirstOrDefault(note => note.Kind == kind);

    /// <summary>
    /// What to post and cancel so the shade matches the board. One notification per kind however many
    /// causes stand behind it; a kind already posted is not posted again; a posted kind whose cause has
    /// gone is cancelled (a notice goes when its cause does).
    /// </summary>
    public static NoticeNotificationPlan Plan(IReadOnlyList<Notice> waiting, IReadOnlySet<NoticeKind> posted)
    {
        ArgumentNullException.ThrowIfNull(waiting);
        ArgumentNullException.ThrowIfNull(posted);

        var present = waiting.Select(notice => notice.Kind).ToHashSet();
        var post = Table.Where(note => present.Contains(note.Kind) && !posted.Contains(note.Kind)).ToList();
        var cancel = Table.Where(note => posted.Contains(note.Kind) && !present.Contains(note.Kind)).Select(note => note.Kind).ToList();
        return new NoticeNotificationPlan(post, cancel);
    }
}
