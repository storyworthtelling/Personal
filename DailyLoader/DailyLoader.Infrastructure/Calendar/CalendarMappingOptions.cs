using DailyLoader.Domain.Models;

namespace DailyLoader.Infrastructure.Calendar;

// Configures how Google calendars map to the five columns. Filled in after the OAuth
// consent lists the available calendar ids.
public class CalendarMappingOptions
{
    public const string SectionName = "CalendarMapping";

    // Calendar id -> column. Sports-team calendars map to Watch; work calendars to the
    // owning person; etc.
    public Dictionary<string, PersonColumn> CalendarColumns { get; set; } = new();

    // The shared Family calendar whose events are routed to Gwen/Eva by title keyword.
    public string FamilyCalendarId { get; set; } = string.Empty;

    // Column used for events on a calendar that isn't explicitly mapped.
    public PersonColumn DefaultColumn { get; set; } = PersonColumn.Matt;

    // Title keywords that route a Family-calendar event to a kid's column during the
    // transition before they have dedicated calendars.
    public List<string> GwenKeywords { get; set; } = ["Gwen"];
    public List<string> EvaKeywords { get; set; } = ["Eva"];
}
