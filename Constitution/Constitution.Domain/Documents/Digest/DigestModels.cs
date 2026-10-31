using Constitution.Domain.Body;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Documents;

/// <summary>One planned session in the week just ended: what it was, how long it was meant to take, how it ended, and the ratings in a word.</summary>
public sealed record DigestSession(DateOnly Day, string Name, int PlannedMinutes, SessionOutcome Outcome, string? RatingSummary);

/// <summary>The week's workouts for one person, and "what moved" as sentences already worded.</summary>
public sealed record DigestWorkoutWeek(DateOnly WeekStart, IReadOnlyList<DigestSession> Sessions, IReadOnlyList<string> Moved);

/// <summary>The household's week at the table, the same for both adults: the loved dish, the stretch dish's verdict and what follows, three facts.</summary>
public sealed record DigestMealsWeek(string? LovedDish, string? StretchDish, string? StretchVerdict, string? StretchFollows, IReadOnlyList<string> Facts);

/// <summary>One line of next week: a day and what is planned for it.</summary>
public sealed record DigestPlannedItem(DateOnly Day, string Text);

/// <summary>Next week as it stands: the planned workouts and the known dinners.</summary>
public sealed record DigestNextWeek(IReadOnlyList<DigestPlannedItem> Workouts, IReadOnlyList<DigestPlannedItem> Dinners);

/// <summary>Everything the Saturday digest is built from, for one adult. Body readings are that adult's alone.</summary>
public sealed record SaturdayDigestModel(
    string Person,
    DateOnly Saturday,
    DigestWorkoutWeek Workouts,
    IReadOnlyList<BodyReading> Readings,
    DigestMealsWeek Meals,
    DigestNextWeek Next);

/// <summary>One signal's last twelve weeks (oldest first); a null week is a gap, never a zero.</summary>
public sealed record DigestSeries(BodySignal Signal, IReadOnlyList<double?> Weeks, double? Latest, string? LatestText, string? ChangeWords)
{
    /// <summary>True when some week has no reading but others do: the line is dotted across it.</summary>
    public bool HasGap => Weeks.Any(week => week is null) && Weeks.Any(week => week is not null);
}

/// <summary>The frozen ids of the digest's ink targets (C-2.6.3).</summary>
public static class DigestIds
{
    /// <summary>The four ruled lines of "Notes to the planner". Frozen: the Sunday run reads them by these ids.</summary>
    public static IReadOnlyList<string> PlannerNotes { get; } =
    [
        "digest-planner-note-1", "digest-planner-note-2", "digest-planner-note-3", "digest-planner-note-4",
    ];
}

/// <summary>When the planner reads the digest's notes: Sunday's morning run, the day after the digest is delivered.</summary>
public static class DigestPlannerNotes
{
    public static DateOnly ReadOn(DateOnly saturday) => saturday.AddDays(1);

    public static bool IsDue(DateOnly saturday, DateOnly today) => today >= ReadOn(saturday);
}
