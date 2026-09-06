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
}
