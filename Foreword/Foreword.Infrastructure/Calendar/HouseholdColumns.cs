using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Storyworthtelling.People;

namespace Foreword.Infrastructure.Calendar;

/// <summary>
/// The household's people (<see cref="HouseholdPeople"/>, which Foreword owns and every app reads) are who this phone's columns are. Each
/// phone keeps its own column order, routing words and calendar answers; the people themselves — who they are, their names, whether
/// they can be invited, their own calendar — come from the household, so both phones print the same people.
/// </summary>
public static class HouseholdColumns
{
    /// <summary>
    /// Brings the mapping in line with the household: a renamed person's column (and every rule naming it) is renamed, a person who left
    /// takes their column, a new person gets one at the end, a column the household doesn't have goes, and each person's account, address
    /// and own calendar are filled in where this phone hasn't answered otherwise. An empty household (one this phone hasn't met) changes
    /// nothing. True when the mapping or schedule changed.
    /// </summary>
    public static bool Apply(CalendarMappingOptions mapping, WorkScheduleOptions work, HouseholdPeople household)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(household);
        if (household.People.Count == 0)
        {
            return false;
        }

        var before = CalendarMappingCodec.Write(mapping) + CalendarMappingCodec.Write(work);

        // Columns made before the household was shared: one of the same name becomes that person's, with its rules.
        foreach (var column in mapping.Columns.Where(column => column.HouseholdId.Length == 0).ToList())
        {
            if ((household.FindByLink(PeopleApps.Foreword, column.Name) ?? household.FindByName(column.Name)) is { } person
                && mapping.Columns.All(other => other.HouseholdId != person.Id))
            {
                column.HouseholdId = person.Id;
            }
        }

        // Renames follow the person.
        foreach (var column in mapping.Columns.Where(column => column.HouseholdId.Length > 0).ToList())
        {
            if (household.Find(column.HouseholdId) is { } person && column.Name != person.Name)
            {
                ColumnEdits.Rename(mapping, work, new PersonColumn(column.Name), person.Name);
            }
        }

        // New people at the end, in the household's order.
        foreach (var person in household.People.Where(person => mapping.Columns.All(column => column.HouseholdId != person.Id)))
        {
            if (ColumnEdits.Add(mapping, person.Name, person.GoogleAddress is not null))
            {
                mapping.Columns[^1].HouseholdId = person.Id;
            }
        }

        // Whoever the household doesn't have goes (after the adds, so the last column is never refused).
        foreach (var column in mapping.Columns.Where(column => column.HouseholdId.Length == 0 || household.Find(column.HouseholdId) is null).ToList())
        {
            ColumnEdits.Remove(mapping, work, new PersonColumn(column.Name));
        }

        foreach (var person in household.People)
        {
            if (mapping.Columns.FirstOrDefault(column => column.HouseholdId == person.Id) is not { } column)
            {
                continue;
            }

            var name = new PersonColumn(column.Name);
            ColumnEdits.SetHasAccount(mapping, name, person.GoogleAddress is not null);
            if (person.GoogleAddress is { } address)
            {
                if (!mapping.PersonAddresses.TryGetValue(name, out var addresses))
                {
                    addresses = [];
                    mapping.PersonAddresses[name] = addresses;
                }

                if (!addresses.Contains(address, StringComparer.OrdinalIgnoreCase))
                {
                    addresses.Add(address);
                }
            }

            if (person.CalendarId is { } calendar && !Answered(mapping, calendar))
            {
                mapping.CalendarColumns[calendar] = name;
            }
        }

        if (household.Rhythm.FamilyCalendarId is { } family && !Answered(mapping, family) && mapping.FamilyCalendarId.Length == 0)
        {
            mapping.FamilyCalendarId = family;
        }

        return before != CalendarMappingCodec.Write(mapping) + CalendarMappingCodec.Write(work);
    }

    /// <summary>
    /// This phone's columns the household doesn't have yet, for the owner's phone to bring into it once. A column without an account is
    /// taken for a child (Foreword's own rule: a child's days arrive on a calendar a parent owns); the household screen corrects it. The
    /// placeholder column of an install that never set any up is not offered.
    /// </summary>
    public static IReadOnlyList<ImportedPerson> Unshared(CalendarMappingOptions mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        return mapping.Columns
            .Where(column => column.HouseholdId.Length == 0 && !string.IsNullOrWhiteSpace(column.Name))
            .Where(column => !IsPlaceholder(column))
            .Select(column => new ImportedPerson(
                column.Name,
                column.Name,
                IsChild: !column.HasAccount,
                Birthdate: null,
                OnPickup: false,
                GoogleAddress: column.HasAccount && mapping.PersonAddresses.TryGetValue(new PersonColumn(column.Name), out var addresses)
                    ? addresses.FirstOrDefault(address => !string.IsNullOrWhiteSpace(address))
                    : null))
            .ToList();
    }

    /// <summary>The household rhythm this phone already knows (its family calendar), for the owner's phone to fill blanks with once.</summary>
    public static HouseholdRhythm Rhythm(CalendarMappingOptions mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        return new HouseholdRhythm { FamilyCalendarId = string.IsNullOrWhiteSpace(mapping.FamilyCalendarId) ? null : mapping.FamilyCalendarId.Trim() };
    }

    /// <summary>
    /// Person id → calendar, for each person with no calendar of their own whose column is fed by exactly one calendar on this phone (the
    /// calendar Foreword made for somebody without an account is the usual one), so the other apps can read that person's days too.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CalendarsToShare(CalendarMappingOptions mapping, HouseholdPeople household)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(household);
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var person in household.People.Where(person => person.CalendarId is null))
        {
            if (mapping.Columns.FirstOrDefault(column => column.HouseholdId == person.Id) is not { } column)
            {
                continue;
            }

            var calendars = mapping.CalendarColumns.Where(pair => pair.Value == new PersonColumn(column.Name)).Select(pair => pair.Key).ToList();
            if (calendars.Count == 1)
            {
                found[person.Id] = calendars[0];
            }
        }

        return found;
    }

    // The one column a fresh install prints before anyone set the columns up (ColumnLayout.Unset), stored by the first edit.
    private static bool IsPlaceholder(ColumnChoice column) =>
        ColumnLayout.Unset.People.Any(placeholder => placeholder == new PersonColumn(column.Name));

    private static bool Answered(CalendarMappingOptions mapping, string calendar) =>
        mapping.CalendarColumns.ContainsKey(calendar)
        || mapping.SplitCalendars.Contains(calendar)
        || mapping.EveryoneCalendars.Contains(calendar)
        || mapping.NotPrintedCalendars.Contains(calendar)
        || string.Equals(mapping.FamilyCalendarId, calendar, StringComparison.Ordinal);
}
