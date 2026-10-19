using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;

namespace Constitution.Domain.Workouts;

/// <summary>What kind of travel a trip is, for the plan.</summary>
public enum TripKind
{
    /// <summary>One or two adults away for work or on their own: the plan goes on, on the road.</summary>
    Work,

    /// <summary>Away for a holiday: the plan is paused for whoever goes.</summary>
    FamilyHoliday,
}

/// <summary>A hotel the calendar names, by the title the calendar gave it. The lookup decides what that means.</summary>
public sealed record HotelStay(string Name);

/// <summary>A run of days away. <see cref="LastDay"/> is the last day itself (not the exclusive end a calendar writes).</summary>
public sealed record Trip(TripKind Kind, DateOnly FirstDay, DateOnly LastDay, HotelStay? Hotel)
{
    public bool Covers(DateOnly day) => day >= FirstDay && day <= LastDay;
}

/// <summary>The trips read from the calendars, per adult, and which calendars could not be read.</summary>
public sealed class TripRead
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<Trip>> trips;

    public TripRead(IReadOnlyDictionary<string, IReadOnlyList<Trip>> trips, IReadOnlyList<UnreadableCalendar> unreadable)
    {
        ArgumentNullException.ThrowIfNull(trips);
        ArgumentNullException.ThrowIfNull(unreadable);
        this.trips = new Dictionary<string, IReadOnlyList<Trip>>(trips, StringComparer.OrdinalIgnoreCase);
        Unreadable = unreadable;
    }

    /// <summary>The calendars that could not be read. A trip on one of them is not seen.</summary>
    public IReadOnlyList<UnreadableCalendar> Unreadable { get; }

    public bool IsComplete => Unreadable.Count == 0;

    /// <summary>The adults who have at least one trip, in no particular order.</summary>
    public IReadOnlyCollection<string> People => trips.Keys.ToList();

    /// <summary>One adult's trips, earliest first; empty for someone with none.</summary>
    public IReadOnlyList<Trip> For(string person) =>
        trips.TryGetValue(person?.Trim() ?? string.Empty, out var found) ? found : [];
}

/// <summary>What the hotel lookup found.</summary>
public abstract record HotelGymResult
{
    /// <summary>The hotel has a gym; <see cref="Equipment"/> is what it holds, as far as the lookup could tell.</summary>
    public sealed record Found(EquipmentSet Equipment) : HotelGymResult;

    /// <summary>The lookup answered and the hotel has no gym.</summary>
    public sealed record NotFound : HotelGymResult;

    /// <summary>The lookup could not answer. Treated like no gym: a gym that was not confirmed is never prescribed.</summary>
    public sealed record Unavailable : HotelGymResult;
}

/// <summary>
/// The seam the head fills from a places lookup. It does the I/O and turns every failure it can name into
/// <see cref="HotelGymResult.Unavailable"/>; an exception it does not understand is a bug and propagates.
/// </summary>
public interface IHotelGymLookup
{
    Task<HotelGymResult> FindAsync(HotelStay hotel, CancellationToken cancellation);
}

/// <summary>
/// The trips with their hotel gyms resolved: everything the adapter needs to know about travel, and nothing it has to go
/// and find out.
/// </summary>
public sealed class TravelPlan
{
    private readonly TripRead read;
    private readonly IReadOnlyDictionary<string, HotelGymResult> gyms;

    private TravelPlan(TripRead read, IReadOnlyDictionary<string, HotelGymResult> gyms)
    {
        this.read = read;
        this.gyms = gyms;
    }

    public TripRead Read => read;

    /// <summary>No travel at all: what a week with nothing on the road looks like.</summary>
    public static TravelPlan None { get; } = new(new TripRead(new Dictionary<string, IReadOnlyList<Trip>>(), []), new Dictionary<string, HotelGymResult>());

    /// <summary>The trips with no gym found for any hotel.</summary>
    public static TravelPlan Of(TripRead read) => new(read ?? throw new ArgumentNullException(nameof(read)), new Dictionary<string, HotelGymResult>());

    /// <summary>The trips with the answers for their hotels (keyed by hotel name, matched without case).</summary>
    public static TravelPlan Of(TripRead read, IReadOnlyDictionary<string, HotelGymResult> gyms) =>
        new(read ?? throw new ArgumentNullException(nameof(read)), new Dictionary<string, HotelGymResult>(gyms ?? throw new ArgumentNullException(nameof(gyms)), StringComparer.OrdinalIgnoreCase));

    /// <summary>Reads the trips and asks the lookup once for each distinct hotel of a work trip.</summary>
    public static async Task<TravelPlan> ResolveAsync(TripRead read, IHotelGymLookup lookup, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(lookup);
        var hotels = read.People
            .SelectMany(read.For)
            .Where(trip => trip.Kind == TripKind.Work && trip.Hotel is not null)
            .Select(trip => trip.Hotel!)
            .DistinctBy(hotel => hotel.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var answers = new Dictionary<string, HotelGymResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var hotel in hotels)
        {
            answers[hotel.Name] = await lookup.FindAsync(hotel, cancellation);
        }

        return Of(read, answers);
    }

    /// <summary>The trip that decides this person's day: a holiday over a work trip when both cover it. Null when they are home.</summary>
    public Trip? On(string person, DateOnly day) =>
        read.For(person)
            .Where(trip => trip.Covers(day))
            .OrderBy(trip => trip.Kind == TripKind.FamilyHoliday ? 0 : 1)
            .FirstOrDefault();

    public bool IsAway(string person, DateOnly day) => On(person, day) is not null;

    /// <summary>The equipment of the trip's hotel gym, only when the lookup found one.</summary>
    public EquipmentSet? GymFor(Trip trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return trip.Hotel is not null && gyms.TryGetValue(trip.Hotel.Name, out var result) && result is HotelGymResult.Found found
            ? found.Equipment
            : null;
    }
}
