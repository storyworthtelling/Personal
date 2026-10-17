using Constitution.Domain.Notices;

namespace Constitution.Domain.Diagnostics;

/// <summary>How many notices of one kind are waiting. The kind and a bounded count: not the notice's cause key, which can name anything.</summary>
public sealed record NoticeCount
{
    /// <summary>The largest count the export carries. A count is a shape; a larger number is not a count of notices.</summary>
    public const int Ceiling = 999;

    public NoticeCount(NoticeKind kind, int count)
    {
        if (!Enum.IsDefined(kind))
        {
            throw DiagnosticsPhrases.Undefined(nameof(kind));
        }
        if (count is < 0 or > Ceiling)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "A count must be between zero and the export's ceiling.");
        }

        Kind = kind;
        Count = count;
    }

    public NoticeKind Kind { get; }

    public int Count { get; }

    /// <summary>
    /// The waiting notices as kinds and counts, in <see cref="NoticeKind"/> order. This is the one door
    /// from a <see cref="NoticeBoard"/> into the export, and it drops <see cref="Notice.CauseKey"/> and
    /// the time on the way through: the key is free text the caller chose, which is exactly what must
    /// not be shared.
    /// </summary>
    public static IReadOnlyList<NoticeCount> Tally(IEnumerable<Notice> notices)
    {
        ArgumentNullException.ThrowIfNull(notices);

        return notices
            .GroupBy(notice => notice.Kind)
            .OrderBy(group => (int)group.Key)
            .Select(group => new NoticeCount(group.Key, Math.Min(group.Count(), Ceiling)))
            .ToList();
    }
}

/// <summary>
/// The counts a Diagnostics export carries: workouts planned and done, notices waiting by kind, and
/// how many recipes are in the box. Recipes are a <i>count only</i>; there is no member that could
/// hold a title or an ingredient, and the shape test fails if one appears.
/// </summary>
public sealed record DiagnosticsCounts
{
    public DiagnosticsCounts(int workoutsPlanned, int workoutsDone, int recipesInBox, IEnumerable<NoticeCount> noticesWaiting)
    {
        ArgumentNullException.ThrowIfNull(noticesWaiting);

        WorkoutsPlanned = Bounded(workoutsPlanned, nameof(workoutsPlanned));
        WorkoutsDone = Bounded(workoutsDone, nameof(workoutsDone));
        RecipesInBox = Bounded(recipesInBox, nameof(recipesInBox));

        var notices = noticesWaiting.ToList();
        if (notices.Select(entry => entry.Kind).Distinct().Count() != notices.Count)
        {
            throw new ArgumentException("Each notice kind may be counted once.", nameof(noticesWaiting));
        }

        NoticesWaiting = notices.OrderBy(entry => (int)entry.Kind).ToList();
    }

    public int WorkoutsPlanned { get; }

    public int WorkoutsDone { get; }

    public int RecipesInBox { get; }

    public IReadOnlyList<NoticeCount> NoticesWaiting { get; }

    private static int Bounded(int count, string parameter) =>
        count is >= 0 and <= NoticeCount.Ceiling
            ? count
            : throw new ArgumentOutOfRangeException(parameter, "A count must be between zero and the export's ceiling.");
}
