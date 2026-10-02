using System.Text.Json;
using Foreword.Domain.Models;

namespace Foreword.Infrastructure.Calendar;

/// <summary>
/// Turns a document the old app wrote into one with a column layout, on the phone, with nothing
/// re-entered — and without the code ever knowing whose columns they are.
///
/// <para>The old shape had a fixed set of people. It named them as the words in its column values,
/// which read straight across as columns, and as property names: one <c>&lt;Name&gt;Keywords</c>
/// list per person. Those lists are found by their <b>shape</b>, so the migration needs no table of
/// names.</para>
///
/// <para>The layout's order is recovered from the same document. Somebody who can be invited — an
/// address entered for them, or their own primary calendar mapped to them, whose id is an email
/// address rather than a <c>…calendar.google.com</c> one — has an account, and those people came
/// first on the page. Everybody else follows in the order the document names them. If that is not
/// the order the owner wants, the Columns screen moves them; the page prints the names, so a column
/// in the wrong place is visible rather than silent.</para>
/// </summary>
public static class CalendarMappingMigration
{
    private const string KeywordsSuffix = "Keywords";

    public static CalendarMappingOptions Apply(CalendarMappingOptions mapping)
    {
        MoveKeywordLists(mapping);

        if (mapping.Columns.Count == 0)
        {
            mapping.Columns = InferColumns(mapping);
        }

        return mapping;
    }

    private static void MoveKeywordLists(CalendarMappingOptions mapping)
    {
        if (mapping.Unrecognised is null)
        {
            return;
        }

        foreach (var (property, value) in mapping.Unrecognised)
        {
            if (property.Length <= KeywordsSuffix.Length
                || !property.EndsWith(KeywordsSuffix, StringComparison.OrdinalIgnoreCase)
                || value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var column = new PersonColumn(property[..^KeywordsSuffix.Length]);
            if (mapping.Keywords.ContainsKey(column))
            {
                continue;
            }

            mapping.Keywords[column] = value.EnumerateArray()
                .Where(word => word.ValueKind == JsonValueKind.String)
                .Select(word => word.GetString()!)
                .ToList();
        }

        mapping.Unrecognised = null;
    }

    private static List<ColumnChoice> InferColumns(CalendarMappingOptions mapping)
    {
        var withAccounts = new List<PersonColumn>();
        var everyone = new List<PersonColumn>();

        void Note(List<PersonColumn> list, PersonColumn column)
        {
            if (!column.IsWatch && column.Name.Length > 0 && !list.Contains(column))
            {
                list.Add(column);
            }
        }

        foreach (var (person, addresses) in mapping.PersonAddresses)
        {
            if (addresses.Any(a => !string.IsNullOrWhiteSpace(a)))
            {
                Note(withAccounts, person);
            }
        }

        foreach (var (calendarId, column) in mapping.CalendarColumns)
        {
            if (IsSomebodysOwnCalendar(calendarId))
            {
                Note(withAccounts, column);
            }
        }

        foreach (var column in mapping.Keywords.Keys)
        {
            Note(everyone, column);
        }

        foreach (var column in mapping.CalendarColumns.Values)
        {
            Note(everyone, column);
        }

        foreach (var column in mapping.PersonAddresses.Keys)
        {
            Note(everyone, column);
        }

        if (mapping.DefaultColumn is { } fallback)
        {
            Note(everyone, fallback);
        }

        return withAccounts
            .Select(c => new ColumnChoice { Name = c.Name, HasAccount = true })
            .Concat(everyone
                .Where(c => !withAccounts.Contains(c))
                .Select(c => new ColumnChoice { Name = c.Name }))
            .ToList();
    }

    // A person's primary calendar has their email address as its id. Secondary, shared, holiday and
    // subscribed calendars all have ids under calendar.google.com.
    private static bool IsSomebodysOwnCalendar(string calendarId) =>
        calendarId.Contains('@')
        && !calendarId.EndsWith(".calendar.google.com", StringComparison.OrdinalIgnoreCase);
}
