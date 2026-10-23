using Constitution.Domain.Placement;

namespace Constitution.Tests.Placement;

/// <summary>
/// A stand-in for Google Calendar: each person's primary calendar as a list of events carrying the
/// hidden key, found by key anywhere in time. It can also play the owner, who moves and deletes events in
/// the Google Calendar app; those two calls are deliberately not on <see cref="ICalendarWriter"/>, because
/// the planner has no business making them.
/// </summary>
internal sealed class FakeCalendar : ICalendarWriter
{
    private readonly List<Stored> events = [];
    private int nextId = 1;

    public int CreateCalls { get; private set; }

    public int FindCalls { get; private set; }

    /// <summary>People whose calendar cannot be looked into (a share not done yet).</summary>
    public HashSet<string> UnavailablePeople { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>People for whom creating an event fails.</summary>
    public HashSet<string> FailingPeople { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Stored> All => events;

    public IReadOnlyList<Stored> Of(string person) => events.Where(item => SamePerson(item.Person, person)).ToList();

    public Stored? By(PlacementKey key, string person) => events.FirstOrDefault(item => item.Key == key && SamePerson(item.Person, person));

    /// <summary>What the calendar holds, as comparable text, to prove a run changed nothing.</summary>
    public string Snapshot() => string.Join("\n", events.OrderBy(item => item.Id, StringComparer.Ordinal).Select(item =>
        $"{item.Id}|{item.Person}|{item.Key.Value}|{item.Title}|{item.Start:O}|{item.End:O}"));

    public void OwnerMoves(PlacementKey key, string person, DateTimeOffset start, DateTimeOffset end)
    {
        var index = events.FindIndex(item => item.Key == key && SamePerson(item.Person, person));
        Assert.True(index >= 0, "The owner tried to move an event that is not on the calendar.");
        events[index] = events[index] with { Start = start, End = end };
    }

    /// <summary>The narrow door a swap uses: the event found by its id is given the key and the time the move names.</summary>
    public void Rekey(string id, PlacementKey key, DateTimeOffset start, DateTimeOffset end)
    {
        var index = events.FindIndex(item => item.Id == id);
        Assert.True(index >= 0, "The tab tried to edit an event that is not on the calendar.");
        events[index] = events[index] with { Key = key, Start = start, End = end };
    }

    public void Remove(string id) => events.RemoveAll(item => item.Id == id);

    public void OwnerDeletes(PlacementKey key, string person)
    {
        var removed = events.RemoveAll(item => item.Key == key && SamePerson(item.Person, person));
        Assert.True(removed > 0, "The owner tried to delete an event that is not on the calendar.");
    }

    public Task<CalendarLookup> FindAsync(string person, IReadOnlyList<PlacementKey> keys, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FindCalls++;
        if (UnavailablePeople.Contains(person))
        {
            return Task.FromResult(CalendarLookup.Unavailable($"{person} has not shared a calendar yet."));
        }

        var wanted = keys.ToHashSet();
        var found = events
            .Where(item => SamePerson(item.Person, person) && wanted.Contains(item.Key))
            .Select(item => new HeldEvent(item.Id, item.Key, item.Start, item.End));
        return Task.FromResult(CalendarLookup.Found(found));
    }

    public Task<CalendarCreate> CreateAsync(EventToCreate placed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CreateCalls++;
        if (FailingPeople.Contains(placed.Person))
        {
            return Task.FromResult(CalendarCreate.Failed($"{placed.Person}'s calendar refused the write."));
        }

        var id = $"event-{nextId++}";
        events.Add(new Stored(id, placed.Person, placed.Key, placed.Title, placed.Start, placed.End));
        return Task.FromResult(CalendarCreate.Created(id));
    }

    private static bool SamePerson(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    public sealed record Stored(string Id, string Person, PlacementKey Key, string Title, DateTimeOffset Start, DateTimeOffset End);
}

/// <summary>The written-keys record held as the text the phone would store, so every save goes through the codec.</summary>
internal sealed class MemoryKeyStore : IWrittenKeyStore
{
    public string? Json { get; set; }

    public int Saves { get; private set; }

    public WrittenKeysRead Load() => WrittenKeysCodec.Decode(Json);

    public void Save(WrittenKeys keys)
    {
        Saves++;
        Json = WrittenKeysCodec.Encode(keys);
    }
}
