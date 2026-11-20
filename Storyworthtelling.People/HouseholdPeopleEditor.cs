using Storyworthtelling.Household;

namespace Storyworthtelling.People;

/// <summary>What an edit came to: the document to keep (the input itself when refused) and the refusal sentence, if any.</summary>
public sealed record PeopleEdit(HouseholdDocument Document, string? Refusal)
{
    public bool Accepted => Refusal is null;
}

/// <summary>
/// The household's one writer: Foreword, on the owner's phone (<paramref name="canEdit"/>). Every other phone's edit is refused with a
/// sentence before anything is built. The one exception to "only Foreword writes" is <see cref="Import"/>, which the owner's install of
/// another app runs once to bring its own people across. Pure: the stamps and the writer's install id come from the caller, who owns the
/// clock. Every write <b>edits</b> a live record rather than replacing it, so links and fields a newer build wrote survive.
/// </summary>
public sealed class HouseholdPeopleEditor(bool canEdit)
{
    public const string ReadOnlySentence = "The household is changed in Foreword, on the owner’s phone.";

    public bool CanEdit { get; } = canEdit;

    public PeopleEdit Put(HouseholdDocument document, HouseholdPerson person, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(person);
        if (!CanEdit)
        {
            return new PeopleEdit(document, ReadOnlySentence);
        }

        return new PeopleEdit(Write(document, PeopleRecords.KeyOf(person.Id), PeopleRecords.FieldsOf(person), stamp, writer), null);
    }

    public PeopleEdit Remove(HouseholdDocument document, string personId, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!CanEdit)
        {
            return new PeopleEdit(document, ReadOnlySentence);
        }

        var key = PeopleRecords.KeyOf(personId);
        var current = document.Find(key);
        if (current is null or { IsTombstone: true })
        {
            return new PeopleEdit(document, null);
        }

        return new PeopleEdit(document.With(current.Delete(stamp, writer)), null);
    }

    /// <summary>Names <paramref name="localId"/> as the person's id in <paramref name="app"/>, changing nothing else.</summary>
    public PeopleEdit Link(HouseholdDocument document, string personId, string app, string localId, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!CanEdit)
        {
            return new PeopleEdit(document, ReadOnlySentence);
        }

        var key = PeopleRecords.KeyOf(personId);
        if (document.Live(key) is not { } current)
        {
            throw new ArgumentException("There is no person '" + personId + "' to link.", nameof(personId));
        }

        var changes = new Dictionary<string, string>(StringComparer.Ordinal) { [PeopleRecords.LinkPrefix + app] = localId };
        return new PeopleEdit(document.With(current.Edit(changes, PeopleRecords.SchemaVersion, stamp, writer)), null);
    }

    public PeopleEdit SetRhythm(HouseholdDocument document, HouseholdRhythm rhythm, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rhythm);
        if (!CanEdit)
        {
            return new PeopleEdit(document, ReadOnlySentence);
        }

        return new PeopleEdit(Write(document, PeopleRecords.RhythmKey, PeopleRecords.FieldsOf(rhythm), stamp, writer), null);
    }

    /// <summary>The rhythm with each unset value taken from <paramref name="known"/>; what is already set is never changed.</summary>
    public PeopleEdit FillRhythm(HouseholdDocument document, HouseholdRhythm known, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(known);
        var current = HouseholdPeople.From(document).Rhythm;
        var filled = new HouseholdRhythm
        {
            FamilyCalendarId = current.FamilyCalendarId ?? (current.NoFamilyCalendar ? null : known.FamilyCalendarId),
            NoFamilyCalendar = current.NoFamilyCalendar || (current.FamilyCalendarId is null && known.FamilyCalendarId is null && known.NoFamilyCalendar),
            DinnerStart = current.DinnerStart ?? known.DinnerStart,
            DinnerLength = current.DinnerLength ?? known.DinnerLength,
            Bedtime = current.Bedtime ?? known.Bedtime,
            ZoneId = current.ZoneId ?? known.ZoneId,
        };
        return filled == current ? new PeopleEdit(document, CanEdit ? null : ReadOnlySentence) : SetRhythm(document, filled, stamp, writer);
    }

    /// <summary>
    /// Brings an app's own people into the household once. A person the app already linked is left alone; a person of the same name is
    /// linked and has only its blank fields filled (never Foreword's choices changed); anyone else is added with the app's own id (or
    /// <c>id-app</c> when that id is taken), so the app's records about them still name them. Running it again changes nothing.
    /// </summary>
    public PeopleEdit Import(HouseholdDocument document, string app, IReadOnlyList<ImportedPerson> people, Func<HlcTimestamp> nextStamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(app);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(nextStamp);
        if (!CanEdit)
        {
            return new PeopleEdit(document, ReadOnlySentence);
        }

        foreach (var incoming in people)
        {
            var household = HouseholdPeople.From(document);
            if (household.FindByLink(app, incoming.LocalId) is not null)
            {
                continue;
            }

            if (household.FindByName(incoming.Name) is { } same && !same.Links.ContainsKey(app))
            {
                var filled = same with
                {
                    Birthdate = same.Birthdate ?? incoming.Birthdate,
                    GoogleAddress = same.GoogleAddress ?? incoming.GoogleAddress,
                    Links = new Dictionary<string, string>(same.Links, StringComparer.Ordinal) { [app] = incoming.LocalId },
                };
                document = Put(document, filled, nextStamp(), writer).Document;
                continue;
            }

            var id = incoming.LocalId.Trim().ToLowerInvariant();
            if (document.Find(PeopleRecords.KeyOf(id)) is not null)
            {
                id = id + "-" + app;
            }

            var added = new HouseholdPerson(id, incoming.Name, incoming.IsChild, incoming.Birthdate, incoming.OnPickup && !incoming.IsChild, incoming.GoogleAddress, null)
            {
                Links = new Dictionary<string, string>(StringComparer.Ordinal) { [app] = incoming.LocalId },
            };
            document = Put(document, added, nextStamp(), writer).Document;
        }

        return new PeopleEdit(document, null);
    }

    private static HouseholdDocument Write(HouseholdDocument document, RecordKey key, IReadOnlyDictionary<string, string> fields, HlcTimestamp stamp, string writer)
    {
        var current = document.Find(key);
        var record = current is null
            ? HouseholdRecord.Write(key, PeopleRecords.SchemaVersion, fields, stamp, writer)
            : current.Edit(fields, PeopleRecords.SchemaVersion, stamp, writer);
        return document.With(record);
    }
}
