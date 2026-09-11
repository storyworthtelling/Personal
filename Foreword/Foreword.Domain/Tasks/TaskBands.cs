using System.Text.RegularExpressions;

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

    public static TaskBand For(ForewordTask task)
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
    /// What the marker at the right of the line says: how the task <b>behaves</b>, not where it came
    /// from (T3). A rule repeats on a simple schedule, a smart task places itself, a once task
    /// happens and is gone — and those are three different things to see coming on a page.
    /// </summary>
    public static string Kind(ForewordTask task) => task.Kind switch
    {
        TaskKind.Rule => "rule",
        TaskKind.Smart => "smart",
        _ => "once",
    };

    /// <summary>
    /// Whole words only. "bin" inside "binder" or "combine" is how a keyword rule quietly files half
    /// the list under Evening, and nothing about the page would look wrong.
    /// </summary>
    private static bool Mentions(string title, IReadOnlyList<string> words) =>
        words.Any(word => Regex.IsMatch(title, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase));
}
