namespace Constitution.Domain.Calendar;

/// <summary>
/// The household's standing rules for time that is never given to anything else, whatever the
/// calendars say: work hours, the family dinner and the children's bedtime routine. These are the
/// half of "busy" that no calendar holds; <see cref="BusyTime"/> keeps them apart from what a
/// calendar does hold, so a screen or a log can say which it is.
///
/// <list type="bullet">
///   <item><b>Work hours</b> come from the schedule Foreword keeps: each enabled shift of the person,
///   weekdays only, an overnight shift running into the next morning. A day with an all-day entry
///   carrying an off keyword has no shift. On a day worked from home (an all-day entry on the
///   person's own calendar with a WFH word) the lunch window inside the shift is free. When the
///   schedule is <i>unknown</i> the hours are <see cref="AssumedWorkStart"/> to
///   <see cref="AssumedWorkEnd"/> on weekdays and nothing is freed, because no off-day words can be
///   trusted either.</item>
///   <item><b>Family dinner</b> is every day, for both adults.</item>
///   <item><b>The bedtime routine</b> is every day, for both adults, up to the bedtime and not after.</item>
/// </list>
///
/// <para>An entry frees time only if it names this person or nobody (so "Lee off" on the family
/// calendar frees Lee's hours, never Sam's). Freeing time is the unsafe direction, so it is narrow
/// where blocking is broad.</para>
/// </summary>
public static class NeverPlacedTime
{
    /// <summary>When assumed work hours open: 8 am, the hour Foreword's own default opens.</summary>
    public static readonly TimeOnly AssumedWorkStart = new(8, 0);

    /// <summary>When assumed work hours close: 4 pm, the hour Foreword's own default closes.</summary>
    public static readonly TimeOnly AssumedWorkEnd = new(16, 0);

    /// <summary>
    /// Every never-placed stretch that touches the days <paramref name="firstDay"/> to
    /// <paramref name="lastDay"/>, merged, <b>not</b> clipped to them: a shift that began the evening
    /// before the first day is included whole. Rules that fall inside a clock change are resolved by
    /// <see cref="ZoneClock"/>. <paramref name="ownEntries"/> and <paramref name="familyEntries"/> are
    /// read only for off-day and WFH markers.
    /// </summary>
    public static IReadOnlyList<BusyInterval> For(
        string person,
        DateOnly firstDay,
        DateOnly lastDay,
        PlanningRules rules,
        IReadOnlyList<CalendarEntry> ownEntries,
        IReadOnlyList<CalendarEntry> familyEntries)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(ownEntries);
        ArgumentNullException.ThrowIfNull(familyEntries);
        var adult = rules.FindAdult(person)
            ?? throw new ArgumentException("The person asked for is not one of the household's adults.", nameof(person));

        var intervals = new List<BusyInterval>();
        var markers = new DayMarkers(adult, rules, ownEntries, familyEntries);

        // One day early, so a shift that began the evening before still reaches the first morning.
        for (var day = firstDay.AddDays(-1); day <= lastDay; day = day.AddDays(1))
        {
            intervals.AddRange(WorkHours(adult, day, rules, markers));
            if (day < firstDay)
            {
                continue;
            }

            Add(intervals, ZoneClock.Window(day, rules.Dinner.Start, rules.Dinner.Length, rules.Zone), BusyReason.FamilyDinner);
            if (!rules.Bedtime.IsNone)
            {
                Add(intervals, ZoneClock.Window(day, rules.Bedtime.Begins, rules.Bedtime.Length, rules.Zone), BusyReason.BedtimeRoutine);
            }
        }

        return Intervals.Merge(intervals);
    }

    private static IEnumerable<BusyInterval> WorkHours(Adult adult, DateOnly day, PlanningRules rules, DayMarkers markers)
    {
        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            yield break;
        }

        // The schedule is unknown: assume the hours, free nothing.
        if (!rules.Work.IsKnown)
        {
            foreach (var assumed in Shift(day, AssumedWorkStart, AssumedWorkEnd, rules.Zone))
            {
                yield return assumed;
            }

            yield break;
        }

        var shifts = rules.Work.ShiftsFor(adult.Name);
        if (shifts.Count == 0 || markers.IsOffDay(day))
        {
            yield break;
        }

        var lunch = markers.IsWorkingFromHome(day) ? ZoneClock.Window(day, rules.Lunch.Start, rules.Lunch.Length, rules.Zone) : ((DateTimeOffset, DateTimeOffset)?)null;
        foreach (var shift in shifts)
        {
            foreach (var piece in Shift(day, shift.Start, shift.End, rules.Zone))
            {
                foreach (var kept in lunch is { } gap ? Without(piece, gap.Item1, gap.Item2) : [piece])
                {
                    yield return kept;
                }
            }
        }
    }

    // A shift that ends earlier on the clock than it starts runs overnight into the next day.
    private static IEnumerable<BusyInterval> Shift(DateOnly day, TimeOnly start, TimeOnly end, TimeZoneInfo zone)
    {
        var opens = ZoneClock.At(day, start, zone, ClockEdge.Start);
        var closes = ZoneClock.At(end > start ? day : day.AddDays(1), end, zone, ClockEdge.End);
        if (closes > opens)
        {
            yield return new BusyInterval(opens, closes, BusyReason.WorkHours);
        }
    }

    // What is left of a stretch once a window is taken out of it: up to two pieces.
    private static IEnumerable<BusyInterval> Without(BusyInterval piece, DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= piece.Start || from >= piece.End)
        {
            yield return piece;
            yield break;
        }

        if (from > piece.Start)
        {
            yield return piece with { End = from };
        }

        if (to < piece.End)
        {
            yield return piece with { Start = to };
        }
    }

    private static void Add(List<BusyInterval> intervals, (DateTimeOffset Start, DateTimeOffset End) window, BusyReason reason)
    {
        if (window.End > window.Start)
        {
            intervals.Add(new BusyInterval(window.Start, window.End, reason));
        }
    }

    /// <summary>What the all-day entries say about each day for one person: a day off, or a day worked from home.</summary>
    private sealed class DayMarkers
    {
        private readonly List<CalendarEntry> offEntries;
        private readonly List<CalendarEntry> homeEntries;

        public DayMarkers(Adult adult, PlanningRules rules, IReadOnlyList<CalendarEntry> own, IReadOnlyList<CalendarEntry> family)
        {
            // Only a known schedule reaches the markers (WorkHours returns an unknown one's assumed hours
            // first), so the off-day words here are the schedule's own. An unknown schedule frees nothing.
            var offWords = rules.Work.OffKeywords;
            offEntries = own.Concat(family)
                .Where(entry => entry.IsAllDay && Applies(entry, adult, rules) && offWords.Any(word => WholeWord.Contains(entry.Title, word)))
                .ToList();
            homeEntries = own
                .Where(entry => entry.IsAllDay && Applies(entry, adult, rules) && rules.Lunch.Keywords.Any(word => WholeWord.Contains(entry.Title, word)))
                .ToList();
        }

        public bool IsOffDay(DateOnly day) => offEntries.Any(entry => Covers(entry, day));

        public bool IsWorkingFromHome(DateOnly day) => homeEntries.Any(entry => Covers(entry, day));

        private static bool Covers(CalendarEntry entry, DateOnly day) =>
            entry.IsAllDay && entry.FirstDay <= day && day < entry.EndDayExclusive;

        // An entry speaks for this adult when it names them, or names nobody at all.
        private static bool Applies(CalendarEntry entry, Adult adult, PlanningRules rules)
        {
            var named = NamedAdults.In(entry, rules.Adults);
            return named.Count == 0 || named.Any(other => string.Equals(other.Name, adult.Name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
