using Foreword.Domain.Models;

namespace Foreword.Infrastructure.Calendar;

// Auto-inserted weekday work blocks, suppressed when an all-day off event is present.
public class WorkScheduleOptions
{
    public const string SectionName = "WorkSchedule";

    public List<WorkShift> Shifts { get; set; } =
    [
        new() { Person = PersonColumn.Matt, Start = "08:00", End = "16:00" },
        new() { Person = PersonColumn.Ashley, Start = "08:00", End = "15:30" }
    ];

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
    private static string Clock(string value) =>
        TimeOnly.TryParseExact(value, "HH:mm", out var time)
            ? (time.Minute == 0 ? time.ToString("h") : time.ToString("h:mm"))
            : value;
}
