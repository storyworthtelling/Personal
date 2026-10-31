using Codex.Domain.Policy;

namespace Codex.Domain.Apps;

/// <summary>The role a lit bookmark wears (DESIGN.md §9's mark hues).</summary>
public enum BookmarkRole
{
    /// <summary>Rose: needs attention now.</summary>
    Problem,

    /// <summary>Slate: worth knowing, nothing is wrong (P13 D91).</summary>
    Reference,

    /// <summary>The quiet, settled tone: a low-importance notification.</summary>
    Resolved,

    /// <summary>Something is waiting: the default.</summary>
    Progress,
}

/// <summary>
/// How a book's ribbon is coloured (P13 C-10.2.1; D83, D91; PC1, PC2). Until Constitution the ribbon read
/// only a notification's importance (high is rose, low is settled, otherwise waiting), which cannot show
/// slate. Constitution now puts its role on the notification as an Intent extra and Flyleaf lets it win,
/// <b>only for Constitution's own package</b>: any app can set an extra on its own notification, and no
/// other book's ribbon is changed by one.
///
/// <para>No count is involved anywhere here (C-10.2.2): the ribbon is lit or it is not, and a role is a word.</para>
/// </summary>
public static class NotificationBookmark
{
    /// <summary>The extra Constitution sets; equal to <c>NoticeNotifications.RoleExtraKey</c>, pinned by a content-read guard.</summary>
    public const string RoleExtraKey = "com.storyworthtelling.constitution.NOTICE_ROLE";

    /// <summary>The three errors: rose.</summary>
    public const string ProblemWire = "problem";

    /// <summary>A scan waiting and the box under ten: slate.</summary>
    public const string ReferenceWire = "reference";

    /// <summary>
    /// The ribbon's role for a package whose strongest notification has this normalised urgency (0 low,
    /// 1 default, 2 high) and this role word (null or empty when it set none). A recognised role wins;
    /// anything else, including a role from another package, keeps the importance mapping.
    /// </summary>
    public static BookmarkRole Decide(string package, int urgency, string? roleWire)
    {
        if (package == PolicyContext.ConstitutionPackage)
        {
            if (roleWire == ProblemWire)
            {
                return BookmarkRole.Problem;
            }

            if (roleWire == ReferenceWire)
            {
                return BookmarkRole.Reference;
            }
        }

        return urgency switch
        {
            2 => BookmarkRole.Problem,
            0 => BookmarkRole.Resolved,
            _ => BookmarkRole.Progress,
        };
    }

    /// <summary>
    /// The strongest recognised role among a package's notifications (problem over reference), or null
    /// when none carries one or the package is not Constitution.
    /// </summary>
    public static string? Strongest(string package, IEnumerable<string?> roleWires)
    {
        ArgumentNullException.ThrowIfNull(roleWires);
        if (package != PolicyContext.ConstitutionPackage)
        {
            return null;
        }

        string? strongest = null;
        foreach (var wire in roleWires)
        {
            if (wire == ProblemWire)
            {
                return ProblemWire;
            }

            if (wire == ReferenceWire)
            {
                strongest = ReferenceWire;
            }
        }

        return strongest;
    }
}
