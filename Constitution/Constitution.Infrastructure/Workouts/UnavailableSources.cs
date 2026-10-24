using Constitution.Domain.Workouts;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// The forecast and air-quality source until a client for them exists: it says, every time, that it cannot read, and the run
/// treats that as "changes nothing" (<see cref="ConditionsRead.Unavailable"/>). It never reports calm weather it did not look at.
/// </summary>
public sealed class UnavailableConditions : IConditionsSource
{
    public const string Reason = "No forecast service is connected yet.";

    public Task<ConditionsRead> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation) =>
        Task.FromResult(ConditionsRead.Unavailable(Reason));
}

/// <summary>
/// The hotel-gym lookup until a places client exists: it never finds one, and an unconfirmed gym is never prescribed
/// (<see cref="HotelGymResult.Unavailable"/> is treated like no gym).
/// </summary>
public sealed class UnavailableHotelGyms : IHotelGymLookup
{
    public Task<HotelGymResult> FindAsync(HotelStay hotel, CancellationToken cancellation) =>
        Task.FromResult<HotelGymResult>(new HotelGymResult.Unavailable());
}
