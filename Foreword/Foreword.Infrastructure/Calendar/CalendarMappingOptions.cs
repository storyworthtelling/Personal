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
    // one person written into the code. A document already on a phone carries the word "Rowan" and
    // goes on meaning exactly that.
    //
    // This document is SHARED — it rides the household vault, which both installs read — so it
    // holds household facts only. Whose phone this is lives in InstallOptions, which never leaves
    // the phone it describes.
    public PersonColumn? DefaultColumn { get; set; }


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
