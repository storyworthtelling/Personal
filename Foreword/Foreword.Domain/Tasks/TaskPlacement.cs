namespace Foreword.Domain.Tasks;

/// <summary>A task on today's sheet, and the line that says why it is there.</summary>
public readonly record struct PrintedTask(ForewordTask Task, string Note);

/// <summary>
/// Which tasks print this morning.
///
/// <para>This is the whole of "smart". The owner writes a rule once and never places it again: a
/// smart task chooses its own day inside a window, from the season, the forecast and its preference.
/// And it explains itself — a list that schedules itself and will not say why is one nobody trusts
/// twice, so every task that moved carries the reason on its line.</para>
///
/// <para>Pure, and asked one day at a time, because that is how the morning run works: the sheet is
/// composed for a date and nothing about the answer may depend on when it was asked.</para>
/// </summary>
public static class TaskPlacement
{
    /// <summary>
    /// <paramref name="dryHours"/> answers how many consecutive hours without rain a day offers.
    /// Zero for a day nobody measured, which is the same answer as a wet one — deliberately, because
    /// a task that needs dry weather and has no forecast has not been shown one.
    /// </summary>
    public static IReadOnlyList<PrintedTask> ForToday(
        IReadOnlyList<ForewordTask> tasks,
        DateOnly today,
        Func<DateOnly, int> dryHours,
        AwayPause away)
    {
        var printed = new List<PrintedTask>();

        foreach (var task in tasks)
        {
            if (Place(task, today, dryHours, away) is { } note)
            {
                printed.Add(new PrintedTask(task, note));
            }
        }

        return printed;
    }

    /// <summary>The note, or null when the task does not print today.</summary>
    private static string? Place(
        ForewordTask task, DateOnly today, Func<DateOnly, int> dryHours, AwayPause away) =>
        task.Kind switch
        {
            TaskKind.Once => Once(task, today),
            TaskKind.Rule => Recurring(away, today) ? null : Rule(task, today),
            _ => Recurring(away, today) ? null : Smart(task, today, dryHours),
        };

    /// <summary>
    /// Whether a recurring task is paused this morning. A task that fires while the owner is away
    /// prints on a page nobody is reading and comes back as a backlog they did not fail to do — so
    /// the mornings simply pass, and nothing here records that they did.
    /// </summary>
    private static bool Recurring(AwayPause away, DateOnly today) =>
        away.PauseRecurring && away.Covers(today);

    // ---- a once task ----

    private static string? Once(ForewordTask task, DateOnly today)
    {
        // Closed in Google's own app. The list screen still shows it struck through so the owner can
        // see why it will not print; the sheet is the day's work, and this is not work any more.
        if (task.CompletedInGoogle)
        {
            return null;
        }

        // No due date means it is simply outstanding, so it prints until it is done. A task with no
        // date that waited for one would wait forever.
        if (task.Due is not { } due)
        {
            return task.Unparsed ? Unread : string.Empty;
        }

        if (due > today)
        {
            return null;
        }

        return due < today ? $"due {due.DayOfWeek}" : string.Empty;
    }

    /// <summary>
    /// The owner's sentence was kept but never read — the network was down when they typed it. It
    /// prints as written rather than waiting to be understood: holding it back until a model has
    /// read it would lose the morning it was written for.
    /// </summary>
    private const string Unread = "not read yet — kept as you wrote it";

    // ---- a rule ----

    private static string? Rule(ForewordTask task, DateOnly today)
    {
        if (!task.Season.Includes(today))
        {
            return null;
        }

        return task.Repeat switch
        {
            Repeat.Daily => string.Empty,
            _ => task.On == today.DayOfWeek ? string.Empty : null,
        };
    }

    // ---- a smart task ----

    /// <summary>
    /// A smart task places itself on <b>one</b> day of its window — the first that qualifies, or its
    /// preferred day when that qualifies too. Printing on every qualifying day would be a task the
    /// owner does twice or ignores once.
    /// </summary>
    private static string? Smart(ForewordTask task, DateOnly today, Func<DateOnly, int> dryHours)
    {
        if (!task.Season.Includes(today) || !InWindow(task.Window, today.DayOfWeek))
        {
            return null;
        }

        var window = Window(task.Window, today);
        var qualifying = window.Where(day => Qualifies(task, day, dryHours)).ToList();

        if (qualifying.Count == 0)
        {
            // Nothing in the window works. Either the preference wins anyway, or the week passes —
            // and which of those it is was the owner's answer when the rule was written, because
            // some things genuinely cannot be done in the rain and printing them anyway is how a
            // list starts being ignored.
            if (task.IfNoDay == NoDayFallback.SkipTheWeek)
            {
                return null;
            }

            var fallbackDay = window.FirstOrDefault(day => day.DayOfWeek == task.Prefers, window[0]);
            return fallbackDay == today ? NoDayNote(task) : null;
        }

        // The preferred day when it qualifies; otherwise the first that does. Waiting for a better
        // one is how a task spends the whole week being deferred to tomorrow.
        var chosen = qualifying.FirstOrDefault(day => day.DayOfWeek == task.Prefers, qualifying[0]);

        if (chosen != today)
        {
            return null;
        }

        return task.Prefers is { } preferred && preferred != today.DayOfWeek
            ? $"moved from {preferred} — not enough dry hours"
            : string.Empty;
    }

    private static string NoDayNote(ForewordTask task) =>
        task.DryHoursNeeded is { } hours
            ? $"no dry {Hours(hours)} this week — printing anyway"
            : "nothing in the window qualified — printing anyway";

    private static string Hours(int hours) => hours == 1 ? "hour" : $"{hours} hours";

    private static bool Qualifies(ForewordTask task, DateOnly day, Func<DateOnly, int> dryHours) =>
        task.DryHoursNeeded is not { } needed || dryHours(day) >= needed;

    private static bool InWindow(TaskWindow window, DayOfWeek day) => window switch
    {
        TaskWindow.Weekend => day is DayOfWeek.Saturday or DayOfWeek.Sunday,
        TaskWindow.Weekdays => day is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
        _ => true,
    };

    /// <summary>
    /// The days of the window <paramref name="today"/> falls in, in order.
    ///
    /// <para>A window is the stretch this task gets to choose from, so it has to be the <i>same</i>
    /// stretch on every morning of it — otherwise Saturday and Sunday would each see a different
    /// window and both could decide they were the day.</para>
    /// </summary>
    private static IReadOnlyList<DateOnly> Window(TaskWindow window, DateOnly today)
    {
        // The week this day belongs to, Monday first — so a weekend is Saturday then Sunday rather
        // than one weekend's Sunday and the next one's Saturday.
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var week = Enumerable.Range(0, 7).Select(monday.AddDays).ToList();

        return [.. week.Where(day => InWindow(window, day.DayOfWeek))];
    }
}
