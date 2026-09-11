using System.Globalization;

namespace Foreword.Domain.Tasks;

/// <summary>One line of a task's rule, written out.</summary>
public readonly record struct TaskRow(string Label, string Value);

/// <summary>
/// A task's rule as fields (T4, and the rows T7/T8 render).
///
/// <para>One function for both screens, deliberately. T4 shows a freshly parsed rule back before
/// anything is saved; T7 is the form for editing it afterwards. If those two disagreed about what a
/// rule says, the owner would accept one thing and find another.</para>
///
/// <para>Only the rows the kind actually uses. That is the answer to the objection against forms: a
/// form that grows to the rule is fine, a form that shows every rule's controls to every task is
/// not. "Vitamins, every day" has no window to place itself in, so it has no window row — and an
/// empty form able to express the lawn rule would carry a dozen controls with eleven of them
/// blank.</para>
/// </summary>
public static class TaskSummary
{
    public static IReadOnlyList<TaskRow> Rows(ForewordTask task) => task.Kind switch
    {
        TaskKind.Smart => Smart(task),
        TaskKind.Rule => Rule(task),
        _ => [new TaskRow("Due", Due(task.Due))],
    };

    private static IReadOnlyList<TaskRow> Smart(ForewordTask task)
    {
        var rows = new List<TaskRow>
        {
            new("Window", Window(task.Window)),
        };

        if (task.Prefers is { } prefers)
        {
            rows.Add(new TaskRow("Prefers", prefers.ToString()));
        }

        // No condition, no row. "0 dry hours" reads as a requirement of none rather than as no
        // requirement at all, which is a different rule.
        if (task.DryHoursNeeded is { } hours)
        {
            rows.Add(new TaskRow("Needs", hours == 1 ? "1 dry hour" : $"{hours} dry hours"));
        }

        rows.Add(new TaskRow("Season", Season(task.Season)));

        // The two consequences the owner would not have said out loud. They are here precisely
        // because they were never typed: a rule whose fallback they have not seen is one that will
        // surprise them in the rain.
        rows.Add(new TaskRow("Decided", "When the window opens"));
        rows.Add(new TaskRow(
            task.DryHoursNeeded is null ? "If no day qualifies" : "If no dry day",
            Fallback(task)));

        return rows;
    }

    private static IReadOnlyList<TaskRow> Rule(ForewordTask task) =>
    [
        new TaskRow("How often", task.Repeat == Repeat.Daily
            ? "Every day"
            : task.On is { } day ? $"Every {day}" : "Every week"),
        new TaskRow("Season", Season(task.Season)),
    ];

    private static string Fallback(ForewordTask task) => task.IfNoDay switch
    {
        NoDayFallback.SkipTheWeek => "Skips the week",
        _ => task.Prefers is { } prefers ? $"Prints {prefers} anyway" : "Prints anyway",
    };

    private static string Window(TaskWindow window) => window switch
    {
        TaskWindow.Weekend => "The weekend",
        TaskWindow.Weekdays => "Weekdays",
        _ => "Any day",
    };

    private static string Season(SeasonRange season) =>
        season.IsAllYear ? "All year" : $"{Month(season.FromMonth)} – {Month(season.ToMonth)}";

    private static string Month(int month) =>
        CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);

    private static string Due(DateOnly? due) =>
        due is { } date
            ? date.ToDateTime(TimeOnly.MinValue).ToString("dddd d MMMM", CultureInfo.InvariantCulture)
            : "No date";
}
