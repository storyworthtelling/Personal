namespace Foreword.Domain.Tasks;

/// <summary>
/// What a task <b>does</b>, not where it came from (T3). One list, three behaviours: a rule repeats
/// on a simple schedule, a smart task places itself, a once task happens and is gone.
/// </summary>
public enum TaskKind
{
    /// <summary>Repeats on a schedule. "Vitamins, every day." "Bins out, the night before collection."</summary>
    Rule,

    /// <summary>Places itself in a window, by conditions. "Mow the lawn, once this weekend, if it's dry."</summary>
    Smart,

    /// <summary>Happens once and is gone. These live in Google Tasks; Foreword mirrors them.</summary>
    Once,
}

/// <summary>How often a rule repeats.</summary>
public enum Repeat
{
    Daily,
    Weekly,
}

/// <summary>The stretch of a week a smart task may place itself in.</summary>
public enum TaskWindow
{
    AnyDay,
    Weekend,
    Weekdays,
}

/// <summary>What a smart task does when no day in its window qualifies.</summary>
public enum NoDayFallback
{
    /// <summary>Print on the preferred day regardless — the rule is a preference, not a condition.</summary>
    PrintAnyway,

    /// <summary>Let the week pass. Some things genuinely cannot be done in the rain.</summary>
    SkipTheWeek,
}

/// <summary>
/// The months a task applies to, inclusive at both ends and allowed to wrap the new year — "November
/// to February" is a real season and a range that only counted upwards would make it empty.
/// </summary>
public readonly record struct SeasonRange(int FromMonth, int ToMonth)
{
    public static SeasonRange AllYear { get; } = new(1, 12);

    public bool IsAllYear => FromMonth == 1 && ToMonth == 12;

    public bool Includes(DateOnly date) =>
        FromMonth <= ToMonth
            ? date.Month >= FromMonth && date.Month <= ToMonth
            : date.Month >= FromMonth || date.Month <= ToMonth;
}

/// <summary>
/// One thing the owner wants to do, in the shape Foreword keeps it.
///
/// <para>A <see cref="TaskKind.Once"/> task is a <b>mirror</b> of a Google task rather than a copy:
/// its title and due date are written back rather than kept here, so the two can never disagree.
/// The rest live only in Foreword, because Google Tasks cannot express any of them.</para>
/// </summary>
public sealed record ForewordTask
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required TaskKind Kind { get; init; }

    // ---- a rule ----
    public Repeat Repeat { get; init; } = Repeat.Daily;

    /// <summary>The day a weekly rule fires. Ignored by a daily one.</summary>
    public DayOfWeek? On { get; init; }

    // ---- a smart task ----
    public TaskWindow Window { get; init; } = TaskWindow.AnyDay;

    /// <summary>The day it would rather be, when more than one qualifies.</summary>
    public DayOfWeek? Prefers { get; init; }

    /// <summary>Consecutive hours without rain it needs. Null when the weather is irrelevant to it.</summary>
    public int? DryHoursNeeded { get; init; }

    public SeasonRange Season { get; init; } = SeasonRange.AllYear;

    public NoDayFallback IfNoDay { get; init; } = NoDayFallback.PrintAnyway;

    // ---- a once task ----
    public string? GoogleTaskId { get; init; }

    /// <summary>
    /// The list it lives in. Half the address — the API addresses a task as
    /// <c>lists/{tasklist}/tasks/{task}</c>, so an edit written back without it goes nowhere.
    /// </summary>
    public string? GoogleListId { get; init; }
    public DateOnly? Due { get; init; }

    /// <summary>
    /// Closed in Google's own app. It still appears in the list, struck through, so the owner can see
    /// <i>why</i> it will not print — a task that simply vanished would read as one Foreword lost.
    /// </summary>
    public bool CompletedInGoogle { get; init; }

    /// <summary>
    /// The owner's sentence was kept but never parsed — the network was down when they typed it. It
    /// prints as a plain once task and is re-read on the next run that reaches Claude, so a rule
    /// typed on a dead network becomes a rule without anyone retyping it.
    /// </summary>
    public bool Unparsed { get; init; }

    /// <summary>What they actually typed, kept so an unparsed task can be read again later.</summary>
    public string Written { get; init; } = string.Empty;
}

/// <summary>
/// The away pause (A1): two dates and three toggles.
///
/// <para>The one thing the owner asked for that Google Tasks cannot express. A recurring task that
/// fires while you are away is worse than useless — it prints on a page you are not reading and
/// comes back as a backlog you did not fail to do. So a paused task is <b>not late and does not pile
/// up</b>: the mornings simply pass, and the one after is ordinary.</para>
///
/// <para>What is <i>not</i> paused matters as much. The morning still goes out by default, because a
/// holiday is exactly when a day sheet earns its place; and anything with a date of its own — a
/// flight, a ranger talk — keeps printing, because it did not come from a rule.</para>
/// </summary>
public sealed record AwayPause
{
    public DateOnly? From { get; init; }
    public DateOnly? Until { get; init; }

    public bool PauseRecurring { get; init; } = true;
    public bool PauseWorkBlocks { get; init; } = true;

    /// <summary>Whether a document goes out at all. Yes by default — see above.</summary>
    public bool StillDeliver { get; init; } = true;

    public static AwayPause None { get; } = new();

    /// <summary>Inclusive at both ends: the last morning away is still a morning away.</summary>
    public bool Covers(DateOnly date) =>
        From is { } from && Until is { } until && date >= from && date <= until;

    /// <summary>How many mornings the trip covers, for the line the screen prints.</summary>
    public int Mornings =>
        From is { } from && Until is { } until && until >= from
            ? until.DayNumber - from.DayNumber + 1
            : 0;
}
