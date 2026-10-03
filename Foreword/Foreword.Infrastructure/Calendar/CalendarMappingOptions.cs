using System.Text.Json;
using System.Text.Json.Serialization;
using Foreword.Domain.Calendar;
using Foreword.Domain.Models;

namespace Foreword.Infrastructure.Calendar;

// Configures how Google calendars map to this account's columns. Filled in after the OAuth
// consent lists the available calendar ids.
public class CalendarMappingOptions
{
    public const string SectionName = "CalendarMapping";

    // Calendar id -> column. Sports-team calendars map to Watch; work calendars to the
    // owning person; etc.
    public Dictionary<string, PersonColumn> CalendarColumns { get; set; } = new();

    // The shared Family calendar whose events are routed by title keyword. Kept as a shorthand for
    // the one that already exists, so a configuration written before D33 still works untouched;
    // SplitCalendars is the general form.
    public string FamilyCalendarId { get; set; } = string.Empty;

    // Split by name, for ANY calendar (D33) — a one-field generalisation of FamilyCalendarId rather
    // than a new mechanism.
    public List<string> SplitCalendars { get; set; } = [];

    // Calendars whose events belong to the whole family: every column, without needing a name in
    // the title. A holiday calendar is the case.
    public List<string> EveryoneCalendars { get; set; } = [];

    // Calendars the owner has decided not to print. Distinct from an UNMAPPED calendar, which is
    // held and named (D34): this one has been looked at and answered, so it says nothing.
    public List<string> NotPrintedCalendars { get; set; } = [];

    // Column used for events on a calendar that isn't explicitly mapped. NULLABLE, and null is the
    // ordinary state of a fresh install: unset means "whoever this phone belongs to" rather than
    // one person written into the code.
    public PersonColumn? DefaultColumn { get; set; }

    // The columns this account prints, in order, and which of them belong to somebody with a
    // Google account of their own. Set up in Foreword's Columns screen; never written into the
    // code. Empty on a fresh install (ColumnLayout.Unset prints) and on a document the old app
    // wrote, until CalendarMappingMigration rebuilds it from that document's own contents.
    public List<ColumnChoice> Columns { get; set; } = [];

    // Title keywords per column, used when nobody is invited to an event on a shared calendar.
    // Every person, not just the ones without accounts: an event on a shared calendar can name
    // anyone, and matching only some of them is how "Alex and Sam to dinner" fell through to the
    // default.
    public Dictionary<PersonColumn, List<string>> Keywords { get; set; } = new();

    // Each person's email addresses. Read FIRST when splitting a shared calendar (D31): an invited
    // address is a fact about who is going, where a name in a title is a heuristic.
    public Dictionary<PersonColumn, List<string>> PersonAddresses { get; set; } = new();

    // Whatever the document carried that this shape does not name — on a document the old app wrote,
    // one "<Name>Keywords" list per person. CalendarMappingMigration moves those into Keywords and
    // clears this, so they are read once and never written back.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unrecognised { get; set; }

    /// <summary>The columns as the routing and the page use them.</summary>
    public ColumnLayout Layout()
    {
        var people = Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => new PersonColumn(c.Name))
            .ToList();

        if (people.Count == 0)
        {
            return ColumnLayout.Unset;
        }

        var withAccounts = Columns
            .Where(c => c.HasAccount && !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => new PersonColumn(c.Name))
            .ToList();

        return new ColumnLayout(people, withAccounts);
    }
}

/// <summary>One column this account prints.</summary>
public class ColumnChoice
{
    public string Name { get; set; } = string.Empty;

    // Whether this person has a Google account of their own, so an invitation to their address
    // can route an event to them. A column for somebody without one is filled by the calendar an
    // event came from instead, and Foreword makes that calendar for them (EnsureColumnCalendars).
    public bool HasAccount { get; set; }
}
