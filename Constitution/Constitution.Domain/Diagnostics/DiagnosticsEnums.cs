namespace Constitution.Domain.Diagnostics;

/// <summary>
/// The parts of the app a Diagnostics export reports on. A closed list: the export names a part by
/// this value and a fixed phrase (<see cref="DiagnosticsPhrases.Of(DiagnosticsComponent)"/>), never by
/// a string the data could have written.
/// </summary>
public enum DiagnosticsComponent
{
    /// <summary>The morning's pages going to the tablet.</summary>
    MorningDelivery,

    /// <summary>Yesterday's ink being read back off the tablet.</summary>
    ReadBack,

    /// <summary>The Body sync worker.</summary>
    BodySync,

    /// <summary>The household file's sync through Drive.</summary>
    HouseholdSync,

    /// <summary>The calendar write.</summary>
    CalendarWrite,
}

/// <summary>
/// How the last attempt at something ended: a shape, never a message. The service's own error text
/// is deliberately not carried — it can echo what was sent.
/// </summary>
public enum DiagnosticsOutcome
{
    /// <summary>It has not run on this install yet.</summary>
    NeverRan,

    /// <summary>It finished.</summary>
    Succeeded,

    /// <summary>The service refused it: a rejected token, a failed permission.</summary>
    Refused,

    /// <summary>The service could not be reached.</summary>
    Unreachable,

    /// <summary>A document was found with no ink rendered on it (R3), so nothing was read.</summary>
    FoundNothingRead,

    /// <summary>Some of it finished and some did not.</summary>
    Partial,
}

/// <summary>Where a delivered document went.</summary>
public enum DeliveryTarget
{
    /// <summary>The owner's reMarkable tablet, at the root.</summary>
    ReMarkable,

    /// <summary>The owner's Google Drive.</summary>
    Drive,
}

/// <summary>
/// What a stored login is worth right now, in the suite's credential-health shape: a working key is
/// a done state, a missing one has never been entered, a rejected one was refused by a live call.
/// </summary>
public enum CredentialState
{
    /// <summary>It is set and a live call with it was not rejected.</summary>
    Working,

    /// <summary>It has not been entered or restored.</summary>
    Missing,

    /// <summary>A live call with it was refused.</summary>
    Rejected,
}

/// <summary>Whose phone this is, as the export reports it. The household's roles, never a person's name.</summary>
public enum InstallRole
{
    /// <summary>The household owner's install.</summary>
    Owner,

    /// <summary>The other adult's install.</summary>
    Partner,

    /// <summary>The role has not been worked out yet: before the passphrase restores the household file.</summary>
    Unknown,
}

/// <summary>The kinds of document Constitution delivers, as far as the export names them.</summary>
public enum DocumentKind
{
    /// <summary>A workout sheet for one day.</summary>
    Workout,

    /// <summary>The weekly meal sheet.</summary>
    Meals,

    /// <summary>The read-back digest.</summary>
    Digest,
}
