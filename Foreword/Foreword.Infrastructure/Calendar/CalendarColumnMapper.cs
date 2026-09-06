using Microsoft.Extensions.Options;
using Foreword.Domain.Calendar;
using Foreword.Domain.Models;

namespace Foreword.Infrastructure.Calendar;

/// <summary>
/// Assigns each event the <b>set</b> of columns it belongs to (D31).
///
/// <para>It used to return one column and stop at the first name it recognised, which is how "Wren
/// and Linden to swimming" became Wren's alone. The decision itself now lives in
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

    public IReadOnlyList<PersonColumn> Resolve(CalendarEvent calendarEvent) =>
        ColumnSet.Resolve(calendarEvent, RoutingFor(calendarEvent.SourceCalendarId));

    private ColumnRouting RoutingFor(string calendarId)
    {
        // Shared: no column of its own, so its events are split by who is named on them. The old
        // FamilyCalendarId is one way to say that; leaving a calendar unmapped is the other, which
        // is what makes D33 a one-field change rather than a new mechanism.
        var isShared = !string.IsNullOrEmpty(options.FamilyCalendarId)
            && calendarId == options.FamilyCalendarId;

        PersonColumn? column = isShared ? null
            : options.CalendarColumns.TryGetValue(calendarId, out var mapped) ? mapped
            : options.DefaultColumn;

        return new ColumnRouting(column, Addresses(), Names(), options.DefaultColumn);
    }

    private IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Addresses() =>
        options.PersonAddresses.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value);

    private IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Names() =>
        new Dictionary<PersonColumn, IReadOnlyList<string>>
        {
            [PersonColumn.Wren] = options.GwenKeywords,
            [PersonColumn.Linden] = options.EvaKeywords,
            [PersonColumn.Rowan] = options.MattKeywords,
            [PersonColumn.Jordan] = options.AshleyKeywords,
        };
}
