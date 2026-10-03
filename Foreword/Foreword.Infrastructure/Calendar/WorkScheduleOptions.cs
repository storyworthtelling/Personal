using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Foreword.Domain.Time;

namespace Foreword.Infrastructure.Calendar;

// Auto-inserted weekday work blocks, suppressed when an all-day off event is present.
public class WorkScheduleOptions
{
    public const string SectionName = "WorkSchedule";

    public List<WorkShift> Shifts { get; set; } = [];

    /// <summary>
    /// What an install that has never saved a schedule starts with: a weekday block for each of the
    /// first two people with an account, whoever this account's columns say they are. The hours are
    /// the ones the app always started with; the people are no longer written into it.
    /// </summary>
    public static WorkScheduleOptions Defaults(ColumnLayout layout) => new()
    {
        Shifts = layout.WithAccounts
            .Take(2)
            .Select((person, i) => new WorkShift
            {
                Person = person,
                Start = "08:00",
                End = i == 0 ? "16:00" : "15:30",
            })
            .ToList(),
    };

    // Title keywords on an all-day event that mark a day off (suppresses the work block and
    // surfaces the day on the outlook).
    public List<string> OffKeywords { get; set; } = ["Off", "PTO", "Vacation"];
}

public class WorkShift
{
    public PersonColumn Person { get; set; }
    public string Start { get; set; } = "08:00";
    public string End { get; set; } = "16:00";
    public string Title { get; set; } = "Work";

    // Whether the block is drawn at all. A shift that is off is kept rather than deleted, so
    // turning it back on does not mean typing the hours again.
    public bool Enabled { get; set; } = true;

    /// <summary>How the row reads on the calendar screen: "Weekdays 8:00 – 4:00".</summary>
    public string Describe() => $"Weekdays {Clock(Start)} – {Clock(End)}";

    // 12-hour, no leading zero, and no ":00" on the hour — DESIGN.md §5's time rule, and how a
    // person says their own working day out loud.
    //
    // The formatting goes through TimeFormats rather than being spelled out here. The version
    // written here passed "h" to TimeOnly.ToString, which throws: a one-character format string is
    // a STANDARD specifier and "h" is not one. Every shift the app ships with starts on the hour,
    // so this threw for all of them and the Calendars screen could not be opened at all.
    //
    // A value that is not a time is shown back rather than thrown over — this row exists so the
    // owner can see and correct what is stored.
    private static string Clock(string value) =>
        TimeOnly.TryParseExact(value, "HH:mm", out var time) ? TimeFormats.ClockShort(time) : value;
}
