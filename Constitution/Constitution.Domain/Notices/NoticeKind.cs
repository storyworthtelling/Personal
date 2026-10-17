namespace Constitution.Domain.Notices;

/// <summary>
/// Every thing that can wait behind Constitution's dog-ear (book p.18; D77, D90, D91). A closed
/// list: a new kind is a decision with a role attached (<see cref="NoticeKinds.RoleOf"/>), never a
/// drift, and the role table in <c>ConstitutionNoticeTests</c> is typed out so adding one fails
/// until both are edited.
/// </summary>
public enum NoticeKind
{
    /// <summary>The morning's pages did not reach the reMarkable: a refused token or a failed upload. Problem.</summary>
    ReMarkableDelivery,

    /// <summary>Health Connect permission was taken back; the charts stop. Problem.</summary>
    HealthConnectRevoked,

    /// <summary>The bed cannot be reached; the line stops ("Waiting for the bed"). Problem.</summary>
    BedOffline,

    /// <summary>A handwritten number was read with low confidence and has two chips to answer it. Caution.</summary>
    LowConfidenceRead,

    /// <summary>A scanned page is waiting to be checked: waiting, not wrong. Progress (teal, D77).</summary>
    ScanToCheck,

    /// <summary>The budget box has fewer than ten left: worth knowing, nothing to do. Reference (slate).</summary>
    BoxUnderTen,

    /// <summary>"This week's groceries are ready", with one Send to Keep button (D90). Reference (slate).</summary>
    KeepHandoff,
}
