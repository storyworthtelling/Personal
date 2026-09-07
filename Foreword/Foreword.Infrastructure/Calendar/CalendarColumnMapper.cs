using Microsoft.Extensions.Options;
using Foreword.Domain.Calendar;
using Foreword.Domain.Models;

namespace Foreword.Infrastructure.Calendar;

/// <summary>
/// Assigns each event the <b>set</b> of columns it belongs to (D31).
///
/// <para>It used to return one column and stop at the first name it recognised, which is how "Gwen
/// and Eva to swimming" became Gwen's alone. The decision itself now lives in
/// <see cref="ColumnSet"/>, which is pure and tested; this reads the configuration and hands it
/// over.</para>
///
/// <para><b>Split-by-name is available to every calendar</b> (D33), not only the one family id: a
/// calendar is <i>shared</i> when it has no column of its own, and any calendar can be left that
/// way. The single <c>FamilyCalendarId</c> is kept as a shorthand for the one that already exists,
/// so an existing configuration keeps working without being rewritten.</para>
/// </summary>
public class CalendarColumnMapper
{
    private readonly CalendarMappingOptions options;

    public CalendarColumnMapper(IOptions<CalendarMappingOptions> options)
    {
        this.options = options.Value;
    }

    public IReadOnlyList<CalendarEvent> Assign(IEnumerable<CalendarEvent> events)
    {
        var list = events.ToList();
        foreach (var calendarEvent in list)
        {
            calendarEvent.Columns = Resolve(calendarEvent);
        }

        return list;
    }

    /// <summary>
    /// The calendars the owner has actually mapped — explicitly, or as the shared one. Anything else
    /// is held rather than printed into the default column (D34).
    /// </summary>
    public IReadOnlySet<string> KnownCalendars()
    {
        var known = new HashSet<string>(options.CalendarColumns.Keys, StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(options.FamilyCalendarId))
        {
            known.Add(options.FamilyCalendarId);
        }

        // A calendar the owner answered is known, whatever the answer was — including "not
        // printed". Holding a calendar they have already told the app to ignore would be nagging.
        known.UnionWith(options.SplitCalendars);
        known.UnionWith(options.EveryoneCalendars);
        known.UnionWith(options.NotPrintedCalendars);

        return known;
    }

    public IReadOnlyList<PersonColumn> Resolve(CalendarEvent calendarEvent) =>
        IsEveryone(calendarEvent.SourceCalendarId)
            ? ColumnSet.People
            : ColumnSet.Resolve(calendarEvent, RoutingFor(calendarEvent.SourceCalendarId));

    private ColumnRouting RoutingFor(string calendarId)
    {
        // Shared: no column of its own, so its events are split by who is named on them. Any
        // calendar can be, which is what makes D33 a one-field change rather than a new mechanism;
        // FamilyCalendarId is the shorthand for the one that already exists.
        if (IsSplit(calendarId))
        {
            return new ColumnRouting(Column: null, Addresses(), Names(), options.DefaultColumn);
        }

        PersonColumn? column = options.CalendarColumns.TryGetValue(calendarId, out var mapped)
            ? mapped
            : options.DefaultColumn;

        return new ColumnRouting(column, Addresses(), Names(), options.DefaultColumn);
    }

    /// <summary>Calendars whose events belong to everybody without anyone being named.</summary>
    public bool IsEveryone(string calendarId) => options.EveryoneCalendars.Contains(calendarId);

    /// <summary>
    /// Calendars the owner has looked at and decided not to print. Distinct from an <b>unmapped</b>
    /// calendar, which is held and named (D34): this one has been answered, so it says nothing.
    /// </summary>
    public bool IsNotPrinted(string calendarId) => options.NotPrintedCalendars.Contains(calendarId);

    private bool IsSplit(string calendarId) =>
        (!string.IsNullOrEmpty(options.FamilyCalendarId) && calendarId == options.FamilyCalendarId)
        || options.SplitCalendars.Contains(calendarId);

    private IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Addresses() =>
        options.PersonAddresses.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value);

    private IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Names() =>
        new Dictionary<PersonColumn, IReadOnlyList<string>>
        {
            [PersonColumn.Gwen] = options.GwenKeywords,
            [PersonColumn.Eva] = options.EvaKeywords,
            [PersonColumn.Matt] = options.MattKeywords,
            [PersonColumn.Ashley] = options.AshleyKeywords,
        };
}
