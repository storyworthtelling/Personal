using Storyworthtelling.Household;

namespace Storyworthtelling.People;

/// <summary>A person record this build could not read, and why. The reason never repeats a value.</summary>
public sealed record UnreadablePerson(string Id, string Reason);

/// <summary>
/// The household as every app reads it: the people, the rhythm, and the person records that could not be read (reported, never
/// dropped silently). Reading is allowed on every phone.
/// </summary>
public sealed record HouseholdPeople(IReadOnlyList<HouseholdPerson> People, HouseholdRhythm Rhythm, IReadOnlyList<UnreadablePerson> Unreadable)
{
    public static HouseholdPeople Empty { get; } = new([], HouseholdRhythm.None, []);

    public IReadOnlyList<HouseholdPerson> Adults => People.Where(person => !person.IsChild).ToList();

    public IReadOnlyList<HouseholdPerson> Children => People.Where(person => person.IsChild).ToList();

    public IReadOnlyList<HouseholdPerson> PickupAdults => People.Where(person => person.OnPickup).ToList();

    /// <summary>The calendars the children's days are on, one per child who has one.</summary>
    public IReadOnlyList<string> ChildrenCalendarIds => Children.Select(child => child.CalendarId).OfType<string>().Distinct().ToList();

    public HouseholdPerson? Find(string id) =>
        People.FirstOrDefault(person => string.Equals(person.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    public HouseholdPerson? FindByName(string name) =>
        People.FirstOrDefault(person => string.Equals(person.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The person an app knows by <paramref name="localId"/>.</summary>
    public HouseholdPerson? FindByLink(string app, string localId) =>
        People.FirstOrDefault(person => person.Links.TryGetValue(app, out var linked) && string.Equals(linked, localId, StringComparison.OrdinalIgnoreCase));

    public static HouseholdPeople From(HouseholdDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var people = new List<HouseholdPerson>();
        var unreadable = new List<UnreadablePerson>();
        foreach (var record in document.LiveRecords.Where(record => record.Key.Kind == PeopleKinds.Person))
        {
            try
            {
                people.Add(PeopleRecords.ReadPerson(record));
            }
            catch (FormatException exception)
            {
                unreadable.Add(new UnreadablePerson(record.Key.Id, exception.Message));
            }
        }

        var rhythm = document.Live(PeopleRecords.RhythmKey) is { } found ? PeopleRecords.ReadRhythm(found) : HouseholdRhythm.None;
        return new HouseholdPeople(people.OrderBy(person => person.Id, StringComparer.Ordinal).ToList(), rhythm, unreadable);
    }
}
