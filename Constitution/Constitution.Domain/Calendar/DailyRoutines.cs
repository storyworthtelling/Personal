namespace Constitution.Domain.Calendar;

/// <summary>Checks shared by the rules that hold a window inside one day.</summary>
internal static class DayWindow
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    /// <summary>Refuses a window with no length, or one that would run past midnight.</summary>
    public static void Check(TimeOnly start, TimeSpan length, string what, string paramName)
    {
        if (length <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(paramName, length, $"{what} has to take some time.");
        }

        if (start.ToTimeSpan() + length > Day)
        {
            throw new ArgumentOutOfRangeException(paramName, length, $"{what} may not run past midnight; it would protect the wrong evening.");
        }
    }

    public static bool EndsAtMidnight(TimeOnly start, TimeSpan length) => start.ToTimeSpan() + length == Day;

    public static TimeOnly EndOf(TimeOnly start, TimeSpan length) =>
        EndsAtMidnight(start, length) ? TimeOnly.MinValue : TimeOnly.FromTimeSpan(start.ToTimeSpan() + length);
}

/// <summary>
/// The family dinner: a window every evening that is never given to anything else. It is the whole
/// household's, so it is never-placed for both adults, weekends included. Dinner time is inferred from
/// the calendars elsewhere (C-6.4.1); this is the window as that settles it.
/// </summary>
public sealed record FamilyDinner
{
    public FamilyDinner(TimeOnly start, TimeSpan length)
    {
        DayWindow.Check(start, length, "Family dinner", nameof(length));
        Start = start;
        Length = length;
    }

    public TimeOnly Start { get; }

    public TimeSpan Length { get; }

    /// <summary>The wall-clock end. Midnight (00:00) when dinner runs up to it; see <see cref="EndsAtMidnight"/>.</summary>
    public TimeOnly End => DayWindow.EndOf(Start, Length);

    /// <summary>True when dinner ends exactly at midnight, so <see cref="End"/> is the next day's 00:00.</summary>
    public bool EndsAtMidnight => DayWindow.EndsAtMidnight(Start, Length);

    /// <summary>
    /// 5:30 to 6:30 pm. <b>An assumption, not a setting:</b> nothing in the plan gives a default, so
    /// this is only for a household that has not said. It is a named value so that using it is a
    /// visible choice at the call site.
    /// </summary>
    public static FamilyDinner Assumed => new(new TimeOnly(17, 30), TimeSpan.FromMinutes(60));
}

/// <summary>
/// The children's bedtime routine: the stretch before the bedtime setting in which an adult is
/// occupied getting them to bed. It runs from <see cref="Begins"/> up to the bedtime and is
/// never-placed for both adults; the evening after the bedtime is the adults' own (a joint session
/// may be placed in it). <see cref="None"/> is a household with no children to put to bed.
///
/// <para>The bedtime setting itself lives in Settings → Household (C-8.1.2, not built yet); the
/// routine's length is not a setting at all, so <see cref="At"/> supplies a usual one.</para>
/// </summary>
public sealed record BedtimeRoutine
{
    /// <summary>The routine length used when only the bedtime is known.</summary>
    public static readonly TimeSpan UsualLength = TimeSpan.FromMinutes(60);

    /// <summary>
    /// A routine ending at <paramref name="bedtime"/>. The bedtime is an evening time: a routine that
    /// would begin before midnight the night before (a bedtime of 12:30 am) is refused.
    /// </summary>
    public BedtimeRoutine(TimeOnly bedtime, TimeSpan length)
    {
        if (length <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "A bedtime routine has to take some time.");
        }

        if (bedtime.ToTimeSpan() < length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bedtime),
                bedtime,
                "The bedtime is an evening time: a routine of this length would begin the day before, and would protect the wrong night.");
        }

        Bedtime = bedtime;
        Length = length;
    }

    private BedtimeRoutine()
    {
        IsNone = true;
    }

    /// <summary>True for <see cref="None"/>.</summary>
    public bool IsNone { get; }

    public TimeOnly Bedtime { get; }

    public TimeSpan Length { get; }

    /// <summary>When the routine begins: the bedtime less its length.</summary>
    public TimeOnly Begins => TimeOnly.FromTimeSpan(Bedtime.ToTimeSpan() - Length);

    /// <summary>No children to put to bed: no routine to protect.</summary>
    public static BedtimeRoutine None { get; } = new();

    /// <summary>The routine for a household that has set only a bedtime, of the <see cref="UsualLength"/>.</summary>
    public static BedtimeRoutine At(TimeOnly bedtime) => new(bedtime, UsualLength);

    /// <summary>7:30 pm, with the usual routine. <b>An assumption, not a setting</b> (see <see cref="FamilyDinner.Assumed"/>).</summary>
    public static BedtimeRoutine Assumed => At(new TimeOnly(19, 30));
}

/// <summary>
/// The lunch gap that is free on a day worked from home. Foreword's schedule has no notion of
/// working from home, so this is Constitution's own: the <see cref="Keywords"/> that mark such a day
/// (an all-day entry on the person's own calendar whose title carries one) and the window inside the
/// shift that is then free to use. An office day has no gap.
/// </summary>
public sealed record WorkFromHomeLunch
{
    public WorkFromHomeLunch(TimeOnly start, TimeSpan length, IReadOnlyList<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        DayWindow.Check(start, length, "A lunch gap", nameof(length));
        Start = start;
        Length = length;
        Keywords = keywords.Where(word => !string.IsNullOrWhiteSpace(word)).Select(word => word.Trim()).ToList();
    }

    public TimeOnly Start { get; }

    public TimeSpan Length { get; }

    public TimeOnly End => DayWindow.EndOf(Start, Length);

    /// <summary>Words or phrases that mark a day as worked from home. None means no day is ever recognised as one.</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>
    /// Noon to one, recognised by "WFH", "Work from home" or "Working from home". <b>An assumption,
    /// not a setting</b> (see <see cref="FamilyDinner.Assumed"/>).
    /// </summary>
    public static WorkFromHomeLunch Assumed =>
        new(new TimeOnly(12, 0), TimeSpan.FromMinutes(60), ["WFH", "Work from home", "Working from home"]);

    /// <summary>No lunch gap is ever freed.</summary>
    public static WorkFromHomeLunch None => new(new TimeOnly(12, 0), TimeSpan.FromMinutes(60), []);
}
