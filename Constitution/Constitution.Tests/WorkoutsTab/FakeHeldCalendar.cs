using Constitution.Domain.Placement;
using Constitution.Domain.Workouts.Screens;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>
/// A stand-in for the owner's primary calendar that can be looked into by key and edited by event id, plus the owner's own
/// hands (move, delete). Placement's writer cannot edit; this is the new narrow door, faked.
/// </summary>
internal sealed class FakeHeldCalendar : ICalendarWriter, IWorkoutEventEditor
{
    private readonly List<Stored> events = [];
    private int nextId = 1;

    public IReadOnlyList<Stored> All => events;

    public bool Unavailable { get; set; }

    public int Edits { get; private set; }

    public int FindCalls { get; private set; }

    public string Add(string person, PlacementKey key, DateTimeOffset start, DateTimeOffset end)
    {
        var id = $"event-{nextId++}";
        events.Add(new Stored(id, person, key, start, end));
        return id;
    }

    public Stored ById(string id) => events.Single(item => item.Id == id);

    public void OwnerMoves(string id, DateTimeOffset start, DateTimeOffset end)
    {
        var index = events.FindIndex(item => item.Id == id);
        events[index] = events[index] with { Start = start, End = end };
    }

    public Task<CalendarLookup> FindAsync(string person, IReadOnlyList<PlacementKey> keys, CancellationToken cancellationToken)
    {
        FindCalls++;
        if (Unavailable)
        {
            return Task.FromResult(CalendarLookup.Unavailable("The calendar could not be looked into."));
        }

        var wanted = keys.ToHashSet();
        return Task.FromResult(CalendarLookup.Found(events
            .Where(item => string.Equals(item.Person, person, StringComparison.OrdinalIgnoreCase) && wanted.Contains(item.Key))
            .Select(item => new HeldEvent(item.Id, item.Key, item.Start, item.End))));
    }

    public Task<CalendarCreate> CreateAsync(EventToCreate placed, CancellationToken cancellationToken) =>
        Task.FromResult(CalendarCreate.Created(Add(placed.Person, placed.Key, placed.Start, placed.End)));

    public Task<EventEdit> MoveAsync(string person, string eventId, PlacementKey newKey, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        Edits++;
        var index = events.FindIndex(item => item.Id == eventId);
        events[index] = events[index] with { Key = newKey, Start = start, End = end };
        return Task.FromResult(EventEdit.Done());
    }

    public Task<EventEdit> RemoveAsync(string person, string eventId, CancellationToken cancellationToken)
    {
        Edits++;
        events.RemoveAll(item => item.Id == eventId);
        return Task.FromResult(EventEdit.Done());
    }

    public sealed record Stored(string Id, string Person, PlacementKey Key, DateTimeOffset Start, DateTimeOffset End);
}
