using System.Text;
using Constitution.Domain.Calendar;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Settings;
using Storyworthtelling.Household;
using Storyworthtelling.People;

namespace Constitution.Domain.Household;

/// <summary>
/// <b>Foreword owns the household</b> (<see cref="HouseholdPeople"/>, one file every app reads). Constitution keeps its roster as a mirror
/// of it, so the many places that read the roster from Constitution's own file (the meals, the taste, the read-back, the pages) read the
/// household without each learning a second file. Each person keeps the id Constitution's records already name them by (the household's
/// <c>link.constitution</c>, written when Constitution's people joined the household), so no taste note, rating or meal answer loses its
/// person. Pure: the stamps and writer come from the caller.
/// </summary>
public static class HouseholdMirror
{
    /// <summary>Constitution's id for a household person: the one it linked, else the household's id made into a shape a person id may take.</summary>
    public static PersonId IdFor(HouseholdPerson person)
    {
        ArgumentNullException.ThrowIfNull(person);
        if (person.Links.TryGetValue(PeopleApps.Constitution, out var linked) && TryId(linked) is { } kept)
        {
            return kept;
        }

        return TryId(person.Id) ?? TryId(Slug(person.Id)) ?? new PersonId("p-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(person.Id)))[..12].ToLowerInvariant());
    }

    /// <summary>The household's people as Constitution's roster people.</summary>
    public static IReadOnlyList<RosterPerson> Roster(HouseholdPeople household)
    {
        ArgumentNullException.ThrowIfNull(household);
        return household.People
            .Select(person => new RosterPerson(
                IdFor(person),
                person.Name.Length > RosterPerson.MaxNameLength ? person.Name[..RosterPerson.MaxNameLength] : person.Name,
                person.IsChild ? MemberRole.Child : MemberRole.Adult,
                person.Birthdate is { } date ? new Birthdate(date) : null,
                !person.IsChild && person.OnPickup,
                person.GoogleAddress))
            .ToList();
    }

    /// <summary>
    /// The document with its roster made the household's: changed people written, people who left removed (a child with the records tied
    /// to them, as the person sheet's removal does). Nothing is written when nothing differs, and an empty household — one this phone hasn't
    /// met, or one Foreword hasn't set up — leaves the roster as it is.
    /// </summary>
    public static HouseholdDocument Apply(HouseholdDocument document, HouseholdPeople household, Func<HlcTimestamp> next, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(household);
        ArgumentNullException.ThrowIfNull(next);
        if (household.People.Count == 0)
        {
            return document;
        }

        var editor = new RosterEditor(RosterRole.Owner);
        var wanted = Roster(household);
        var current = HouseholdRecords.Read(document, RosterMaps.Person).Items.ToDictionary(person => person.Id);
        foreach (var person in wanted)
        {
            if (!current.TryGetValue(person.Id, out var had) || had != person)
            {
                document = editor.Save(document, person, next(), writer).Document;
            }
        }

        var keep = wanted.Select(person => person.Id).ToHashSet();
        foreach (var gone in current.Keys.Where(id => !keep.Contains(id)))
        {
            document = RosterRemoval.Remove(editor, document, gone, next(), writer).Document;
        }

        return document;
    }

    /// <summary>The people Constitution's own file holds, for the owner's phone to bring into the household once.</summary>
    public static IReadOnlyList<ImportedPerson> ForImport(HouseholdDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return HouseholdRecords.Read(document, RosterMaps.Person).Items
            .Select(person => new ImportedPerson(
                person.Id.Value, person.Name, person.Role == MemberRole.Child, person.Birthdate?.Date, person.OnPickup, person.GoogleAddress))
            .ToList();
    }

    /// <summary>
    /// The week's choices from the household: each adult's calendar and pickup, the family and children's calendars, dinner, bedtime and
    /// the zone. Who this phone delivers for is this phone's own (each phone is paired to its own tablet), so it is kept from
    /// <paramref name="thisPhone"/>.
    /// </summary>
    public static HouseholdChoices Choices(HouseholdPeople household, HouseholdChoices thisPhone)
    {
        ArgumentNullException.ThrowIfNull(household);
        ArgumentNullException.ThrowIfNull(thisPhone);
        var calendars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pickup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var adult in household.Adults)
        {
            if (adult.CalendarId is { } calendar)
            {
                calendars[adult.Name] = calendar;
            }

            pickup[adult.Name] = adult.OnPickup;
        }

        var rhythm = household.Rhythm;
        return new HouseholdChoices
        {
            Bedtime = rhythm.Bedtime,
            DinnerStart = rhythm.DinnerStart,
            DinnerLength = rhythm.DinnerLength,
            ZoneId = rhythm.ZoneId,
            FamilyCalendarId = rhythm.FamilyCalendarId,
            NoFamilyCalendar = rhythm.NoFamilyCalendar,
            ChildrenCalendarIds = household.ChildrenCalendarIds,
            NoChildren = household.Children.Count == 0,
            DeliversHere = thisPhone.DeliversHere,
            AdultCalendars = calendars,
            Pickup = pickup,
        };
    }

    /// <summary>What this phone's setup already knew of the household's rhythm, for the owner's phone to fill Foreword's blanks with once.</summary>
    public static HouseholdRhythm Rhythm(PlanningSetup? setup) => setup is null
        ? HouseholdRhythm.None
        : new HouseholdRhythm
        {
            FamilyCalendarId = setup.FamilyCalendarId,
            NoFamilyCalendar = setup.NoFamilyCalendar,
            DinnerStart = setup.DinnerStart,
            DinnerLength = setup.DinnerLength,
            Bedtime = setup.Bedtime,
            ZoneId = setup.ZoneId,
        };

    private static PersonId? TryId(string value)
    {
        try
        {
            return new PersonId(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length > PersonId.MaxLength ? slug[..PersonId.MaxLength].Trim('-') : slug;
    }
}
