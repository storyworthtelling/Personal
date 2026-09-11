using System.Globalization;
using System.Text.RegularExpressions;
using Foreword.Domain.Models;

namespace Foreword.Domain.Tasks;

/// <summary>The checklist's three time bands, in the order the day runs.</summary>
public enum TaskBand
{
    Morning,
    DuringTheDay,
    Evening,
}

/// <summary>
/// Which band a task belongs in, and what the line says about it.
///
/// <para>Inferred rather than set by hand (Q63): nobody is going to tag every task with a time of
/// day, and a list that all lands in one band is the single column the three-band layout exists to
/// avoid. The inference is deliberately a short list of obvious words — a task that does not
/// announce itself belongs in the middle of the day, which is where most things actually are.</para>
/// </summary>
public static partial class TaskBands
{
    private static readonly string[] MorningWords =
        ["morning", "vitamins", "breakfast", "brief", "coffee", "gym", "school run", "pack"];

    private static readonly string[] EveningWords =
        ["evening", "tonight", "bins", "dinner", "bedtime", "lock up", "trash"];

    public static TaskBand For(TodoItem task)
    {
        if (Mentions(task.Title, MorningWords)) return TaskBand.Morning;
        if (Mentions(task.Title, EveningWords)) return TaskBand.Evening;

        return TaskBand.DuringTheDay;
    }

    public static string Heading(TaskBand band) => band switch
    {
        TaskBand.Morning => "Morning",
        TaskBand.DuringTheDay => "During the day",
        _ => "Evening",
    };

    /// <summary>
    /// What the line says about a task that is not new. A list that silently re-presents yesterday's
    /// undone work looks like a list that invented it.
    ///
    /// <para>Past a week the day name is dropped for a date: "moved from Tuesday" about something
    /// eleven days old reads as <i>this</i> Tuesday, which is worse than saying nothing.</para>
    /// </summary>
    public static string Note(TodoItem task, DateOnly today)
    {
        if (task.Due is not { } due || due >= today)
        {
            return string.Empty;
        }

        return today.DayNumber - due.DayNumber <= 7
            ? $"moved from {due.DayOfWeek}"
            : $"moved from {due.ToDateTime(TimeOnly.MinValue).ToString("MMM d", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Standing, smart or a one-off. There is nothing to read it from yet — a Google Task is a title
    /// and a date — so it is empty rather than guessed. Labelling every line "task" would be a
    /// column of one repeated word, which is worse than no column.
    /// </summary>
    public static string Kind(TodoItem task) => string.Empty;

    /// <summary>
    /// Whole words only. "bin" inside "binder" or "combine" is how a keyword rule quietly files half
    /// the list under Evening, and nothing about the page would look wrong.
    /// </summary>
    private static bool Mentions(string title, IReadOnlyList<string> words) =>
        words.Any(word => Regex.IsMatch(title, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase));
}
