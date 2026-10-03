using Foreword.Domain.Calendar;
using Foreword.Domain.Tasks;
using Foreword.Domain.Weather;

namespace Foreword.Domain.Models;

/// <summary>
/// Everything the one four-page Day document is composed from.
///
/// <para>It was two documents — a calendar and a checklist, uploaded separately — and P9 binds them
/// into one with a weather page and the outlook. Four files on a tablet are four things to find in
/// a list; one tabbed document is one thing to open.</para>
/// </summary>
public sealed record DayDocument
{
    public required DateOnly Date { get; init; }
    public required DayAgenda Agenda { get; init; }

    /// <summary>
    /// The columns this morning prints, left to right: this account's people, then Watch. They come
    /// from the account's own setup in Foreword, so the page never knows anybody's name in advance.
    /// </summary>
    public IReadOnlyList<PersonColumn> Columns { get; init; } = ColumnLayout.Unset.Printed;

    /// <summary>The seven-day outlook: non-routine events only.</summary>
    public IReadOnlyList<CalendarEvent> Outlook { get; init; } = [];

    /// <summary>
    /// What prints this morning, already placed — the smart tasks have chosen their day and each
    /// carries the line that says why it is here.
    /// </summary>
    public IReadOnlyList<PrintedTask> Tasks { get; init; } = [];

    /// <summary>What came back from the weather calls, which may be nothing at all.</summary>
    public WeatherReport Weather { get; init; } = WeatherReport.Nothing;

    /// <summary>Yesterday's handwriting that could not be read, listed for re-entry.</summary>
    public IReadOnlyList<ParsedNote> UnreadableCarryovers { get; init; } = [];

    /// <summary>
    /// The narrator's line at the top of the day, in the owner's own hand. Empty when there is no
    /// voice profile or the call did not come back — the page simply has no line, rather than a
    /// neutral sentence pretending to be one.
    /// </summary>
    public string Voice { get; init; } = string.Empty;

    /// <summary>The line at the top of the checklist page, same rule as <see cref="Voice"/>.</summary>
    public string ChecklistVoice { get; init; } = string.Empty;
}
