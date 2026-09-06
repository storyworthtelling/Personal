using Foreword.Domain.Models;

namespace Foreword.Infrastructure.Calendar;

// Configures how Google calendars map to the five columns. Filled in after the OAuth
// consent lists the available calendar ids.
public class CalendarMappingOptions
{
    public const string SectionName = "CalendarMapping";

    // Calendar id -> column. Sports-team calendars map to Watch; work calendars to the
    // owning person; etc.
    public Dictionary<string, PersonColumn> CalendarColumns { get; set; } = new();

    // The shared Family calendar whose events are routed to Wren/Linden by title keyword.
    public string FamilyCalendarId { get; set; } = string.Empty;

    // Column used for events on a calendar that isn't explicitly mapped.
    public PersonColumn DefaultColumn { get; set; } = PersonColumn.Rowan;

    // Title keywords per person, used when nobody is invited to an event on a shared calendar.
    // All four, not just the children: an event on a shared calendar can name either parent, and
    // matching only two of the four is how "Rowan and Jordan to dinner" fell through to the default.
    public List<string> GwenKeywords { get; set; } = ["Wren"];
    public List<string> EvaKeywords { get; set; } = ["Linden"];
    public List<string> MattKeywords { get; set; } = ["Rowan"];
    public List<string> AshleyKeywords { get; set; } = ["Jordan"];

    // Each person's email addresses. Read FIRST when splitting a shared calendar (D31): an invited
    // address is a fact about who is going, where a name in a title is a heuristic.
    public Dictionary<PersonColumn, List<string>> PersonAddresses { get; set; } = new();
}
