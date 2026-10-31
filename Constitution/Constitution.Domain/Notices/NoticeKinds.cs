using Codex.Domain.Design;

namespace Constitution.Domain.Notices;

/// <summary>
/// The role each <see cref="NoticeKind"/> wears (D77: errors rose, a low-confidence read ochre, a
/// waiting scan teal, the box under ten and the Keep hand-off slate). The roles are the suite's
/// <see cref="FoldRole"/>, by the same names the MAUI kit's <c>DogEarHue</c> uses, so how serious one
/// is stays <see cref="NoticeSeriousness"/>'s call and is not restated here.
///
/// <para>No kind maps to <see cref="FoldRole.Action"/>: that pine belongs to the undo alone
/// (<see cref="NoticeBoard.UndoRole"/>), so a waiting notice can never be mistaken for the one fold
/// that turns back an act.</para>
/// </summary>
public static class NoticeKinds
{
    /// <summary>Every kind, in declaration order.</summary>
    public static IReadOnlyList<NoticeKind> All { get; } = Enum.GetValues<NoticeKind>();

    /// <summary>The role a notice of this kind wears on the fold and on the Waiting page's bar.</summary>
    public static FoldRole RoleOf(NoticeKind kind) => kind switch
    {
        NoticeKind.ReMarkableDelivery => FoldRole.Problem,
        NoticeKind.HealthConnectRevoked => FoldRole.Problem,
        NoticeKind.BedOffline => FoldRole.Problem,
        NoticeKind.BedSilent => FoldRole.Reference,
        NoticeKind.LowConfidenceRead => FoldRole.Caution,
        NoticeKind.ScanToCheck => FoldRole.Progress,
        NoticeKind.BoxUnderTen => FoldRole.Reference,
        NoticeKind.KeepHandoff => FoldRole.Reference,
        NoticeKind.NoPenMarks => FoldRole.Caution,
        NoticeKind.WeekNotPlanned => FoldRole.Problem,
        NoticeKind.HouseholdFileUnreadable => FoldRole.Problem,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "This notice kind has no role; add it to NoticeKinds.RoleOf and the role table in ConstitutionNoticeTests."),
    };
}
