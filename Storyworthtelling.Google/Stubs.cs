namespace Storyworthtelling.Google;

// Seams stubbed now, implemented in a later phase — the interfaces exist so consumers can depend on the
// shape while the implementations wait for the phase that actually needs them (and for device/live
// verification). No implementation ships here on purpose.

/// <summary>
/// A daily read of the owner's body metrics from Android Health Connect. Health Connect is an on-device
/// Android API (not Google OAuth), so its implementation lands with the flagship (Constitution, P6–7) that
/// actually reads body data — it is stubbed here only so the broker's surface names it.
/// </summary>
public interface IHealthData
{
    Task<HealthSnapshot?> GetDailyAsync(DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>A day's passive body metrics; trends only, never a nag. Shape firmed up when Constitution consumes it.</summary>
public sealed record HealthSnapshot(int? Steps, int? RestingHeartRate, double? SleepHours);

/// <summary>
/// The grocery list home. Intended to be Google Keep, which has <b>no official public API</b> — so this is a
/// seam only; the flagship's grocery-write feature will spike the (unofficial) access and decide the real
/// backing then. Deliberately unimplemented here.
/// </summary>
public interface IGroceryList
{
    Task AddItemsAsync(IReadOnlyList<string> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetItemsAsync(CancellationToken cancellationToken = default);
}
