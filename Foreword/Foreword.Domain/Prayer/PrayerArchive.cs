namespace Foreword.Domain.Prayer;

/// <summary>Everything the private archive holds: what is being held, and what was written.</summary>
public sealed record PrayerArchive
{
    public IReadOnlyList<PrayerRequest> Requests { get; init; } = [];

    public IReadOnlyList<JournalEntry> Journal { get; init; } = [];

    public IReadOnlyList<PrayerRotation> Rotations { get; init; } = [];
}

/// <summary>
/// The four ways opening a passphrase-encrypted file can go, told apart because they need different
/// answers: an absent file is a first morning, a wrong passphrase is something to re-enter, and a
/// corrupt one is something to stop and look at.
/// </summary>
public enum PrayerArchiveStatus
{
    Empty,
    Loaded,
    WrongPassphrase,
    Corrupt,
}

/// <summary>What came back from the archive, and which of the four ways it went if it did not.</summary>
public readonly record struct PrayerArchiveRead(PrayerArchiveStatus Status, PrayerArchive Archive)
{
    public static PrayerArchiveRead Nothing(PrayerArchiveStatus status) => new(status, new PrayerArchive());
}
