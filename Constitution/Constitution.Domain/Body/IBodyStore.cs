namespace Constitution.Domain.Body;

/// <summary>
/// Where one person's Body readings live on this install. The reader writes to it, the charts and
/// the signal order read from it, and the encrypted backup copies it out and, on an empty install,
/// back in. Body never leaves the install except through that backup (D98, G13), so this seam has
/// no member that exports anything.
/// </summary>
public interface IBodyStore
{
    /// <summary>True when no reading has ever been stored: the only state a backup may restore onto.</summary>
    bool IsEmpty { get; }

    /// <summary>
    /// The day Body started reading on this install, or null before the first reading. Nothing earlier
    /// is ever read (no backfill), and a restored backup carries its own install day with it.
    /// </summary>
    DateOnly? InstallDay { get; }

    /// <summary>Every stored reading, one per signal per day, oldest first.</summary>
    IReadOnlyList<BodyReading> All();

    /// <summary>Adds readings; a later reading of the same signal and day replaces the earlier one.</summary>
    void Upsert(IEnumerable<BodyReading> readings);

    /// <summary>Records the install day once; later calls never move it.</summary>
    void SetInstallDay(DateOnly day);
}
