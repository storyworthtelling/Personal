using Foreword.Domain.Models;

namespace Foreword.Infrastructure.Calendar;

/// <summary>
/// Setting up the columns: what the Columns screen does to the stored settings.
///
/// <para>A column's name is also how every rule refers to it — a calendar mapped to it, the words
/// that route to it, the addresses invited as it, the default, a work shift drawn in it. So renaming
/// a column renames it everywhere, and removing one removes its rules, or the settings quietly point
/// at a column that no longer exists and the events vanish from the page with nothing said.</para>
///
/// <para>Each edit answers whether it happened, because the screen has to know whether to change: a
/// refused edit (a blank name, a name already taken, Watch) leaves everything as it was.</para>
/// </summary>
public static class ColumnEdits
{
    public static bool Add(CalendarMappingOptions mapping, string name, bool hasAccount)
    {
        var trimmed = name.Trim();
        if (!Available(mapping, trimmed))
        {
            return false;
        }

        Materialise(mapping);
        mapping.Columns.Add(new ColumnChoice { Name = trimmed, HasAccount = hasAccount });
        return true;
    }

    public static bool Rename(
        CalendarMappingOptions mapping, WorkScheduleOptions work, PersonColumn from, string to)
    {
        var trimmed = to.Trim();
        var definition = Find(mapping, from);
        if (definition is null)
        {
            return false;
        }

        // A change of case only is still a rename — the page prints the name as written.
        if (!string.Equals(definition.Name, trimmed, StringComparison.OrdinalIgnoreCase)
            && !Available(mapping, trimmed))
        {
            return false;
        }

        if (trimmed.Length == 0 || new PersonColumn(trimmed).IsWatch)
        {
            return false;
        }

        var renamed = new PersonColumn(trimmed);
        definition.Name = trimmed;

        foreach (var calendarId in mapping.CalendarColumns.Where(p => p.Value == from).Select(p => p.Key).ToList())
        {
            mapping.CalendarColumns[calendarId] = renamed;
        }

        Rekey(mapping.Keywords, from, renamed);
        Rekey(mapping.PersonAddresses, from, renamed);

        if (mapping.DefaultColumn == from)
        {
            mapping.DefaultColumn = renamed;
        }

        foreach (var shift in work.Shifts.Where(s => s.Person == from))
        {
            shift.Person = renamed;
        }

        return true;
    }

    /// <summary>
    /// Removes a column and every rule that named it. A calendar that fed it becomes unmapped, which
    /// Foreword already holds and names on the page (D34) until it is answered again — rather than
    /// being quietly re-pointed at somebody else. The last column cannot go: a page with no people
    /// on it is not a calendar.
    /// </summary>
    public static bool Remove(CalendarMappingOptions mapping, WorkScheduleOptions work, PersonColumn column)
    {
        Materialise(mapping);
        var definition = Find(mapping, column);
        if (definition is null || mapping.Columns.Count <= 1)
        {
            return false;
        }

        mapping.Columns.Remove(definition);

        foreach (var calendarId in mapping.CalendarColumns.Where(p => p.Value == column).Select(p => p.Key).ToList())
        {
            mapping.CalendarColumns.Remove(calendarId);
        }

        mapping.Keywords.Remove(column);
        mapping.PersonAddresses.Remove(column);

        if (mapping.DefaultColumn == column)
        {
            mapping.DefaultColumn = null;
        }

        work.Shifts.RemoveAll(s => s.Person == column);
        return true;
    }

    /// <summary>Moves a column left (negative) or right (positive) on the page.</summary>
    public static bool Move(CalendarMappingOptions mapping, PersonColumn column, int delta)
    {
        var definition = Find(mapping, column);
        if (definition is null)
        {
            return false;
        }

        var from = mapping.Columns.IndexOf(definition);
        var to = from + delta;
        if (delta == 0 || to < 0 || to >= mapping.Columns.Count)
        {
            return false;
        }

        mapping.Columns.RemoveAt(from);
        mapping.Columns.Insert(to, definition);
        return true;
    }

    /// <summary>
    /// Whether this person can be invited. Saying they cannot also drops the addresses entered for
    /// them: an address nobody is ever invited as matches nothing and never says so.
    /// </summary>
    public static void SetHasAccount(CalendarMappingOptions mapping, PersonColumn column, bool hasAccount)
    {
        Materialise(mapping);
        if (Find(mapping, column) is not { } definition)
        {
            return;
        }

        definition.HasAccount = hasAccount;
        if (!hasAccount)
        {
            mapping.PersonAddresses.Remove(column);
        }
    }

    // A fresh install prints ColumnLayout.Unset without having stored it. The first edit stores it,
    // so "rename Me to my own name" renames something real.
    private static void Materialise(CalendarMappingOptions mapping)
    {
        if (mapping.Columns.Count > 0)
        {
            return;
        }

        var unset = mapping.Layout();
        mapping.Columns = unset.People
            .Select(p => new ColumnChoice { Name = p.Name, HasAccount = unset.HasAccount(p) })
            .ToList();
    }

    private static ColumnChoice? Find(CalendarMappingOptions mapping, PersonColumn column)
    {
        Materialise(mapping);
        return mapping.Columns.FirstOrDefault(c => new PersonColumn(c.Name) == column);
    }

    private static bool Available(CalendarMappingOptions mapping, string name) =>
        name.Length > 0
        && !new PersonColumn(name).IsWatch
        && mapping.Columns.All(c => new PersonColumn(c.Name) != new PersonColumn(name));

    private static void Rekey<T>(Dictionary<PersonColumn, T> map, PersonColumn from, PersonColumn to)
    {
        if (map.Remove(from, out var value))
        {
            map[to] = value;
        }
    }
}
