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

    /// <summary>The bed has not reported for a few days and nothing says why: waiting, not wrong. Reference.</summary>
    BedSilent,

    /// <summary>A handwritten number was read with low confidence and has two chips to answer it. Caution.</summary>
    LowConfidenceRead,

    /// <summary>A scanned page is waiting to be checked: waiting, not wrong. Progress (teal, D77).</summary>
    ScanToCheck,

    /// <summary>The budget box has fewer than ten left: worth knowing, nothing to do. Reference (slate).</summary>
    BoxUnderTen,

    /// <summary>"This week's groceries are ready", with one Send to Keep button (D90). Reference (slate).</summary>
    KeepHandoff,

    /// <summary>
    /// Yesterday's page came back from the tablet's cloud with no pen marks on it, and no page has ever shown any (R3):
    /// either the cloud is not passing the ink along or nothing was written. Waiting, nothing to press. Caution.
    /// </summary>
    NoPenMarks,

    /// <summary>
    /// The week could not be planned: a calendar it needs could not be read (not set up, not shared, would not answer), or a
    /// record it learns from could not be read, so nothing was planned, written to a calendar or sent to the tablet. It looks
    /// again at the next run, so there is nothing to press. Problem.
    /// </summary>
    WeekNotPlanned,

    /// <summary>
    /// The shared household file exists but will not open on this phone (the vault passphrase is missing or does not match, or the
    /// file is damaged or newer than this app), so the recipe box refuses edits until it does (D108, D109). The owner has to act
    /// (fix the passphrase, update the app), and the cause key carries which reason it is. Problem, like the other connection
    /// failures that stop a feature until the owner mends them.
    /// </summary>
    HouseholdFileUnreadable,
}
