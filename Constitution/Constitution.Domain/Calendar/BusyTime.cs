namespace Constitution.Domain.Calendar;

/// <summary>A calendar the model could not read, and why. Said plainly so the head can show it on the Waiting page.</summary>
public sealed record UnreadableCalendar(CalendarSource Source, string Reason);

/// <summary>
/// Every reason one adult is not free over a run of days, in two lists that are never mixed:
/// <see cref="Busy"/> is what a calendar holds (their own, the family's, and the children's events
/// they are on the hook for) and <see cref="NeverPlaced"/> is what the household's rules keep clear
/// (work hours, family dinner, the children's bedtime routine). <see cref="Blocked"/> is both together
/// for finding gaps. Nothing placed by the planner may overlap <see cref="Blocked"/>; that is the
/// rule the whole calendar-placement feature (R7) rests on.
///
/// <para><b>What counts as busy</b> (C-5.3.1):
/// <list type="bullet">
///   <item>The person's <b>own calendar</b>: every entry not marked free.</item>
///   <item>The <b>family calendar</b>: every entry not marked free, for both adults. It is not narrowed by
///   a name in the title; "Lee's parents visit" is Sam's evening too.</item>
///   <item>The <b>children's events</b>, by who they name (a guest address that is theirs, or a name standing
///   alone in the title). An event naming a parent blocks that parent, and only them; one naming both blocks
///   both; <b>one naming nobody blocks every adult on pickup</b>. The pickup flag decides who an unnamed
///   event belongs to and nothing else: a named parent is blocked on or off pickup, because an event with
///   their name on it puts them there whatever the rota says. (That last clause is the one place this
///   departs from the acceptance's literal wording, and it departs toward blocking.)</item>
/// </list>
/// Entries marked <see cref="Availability.Free"/> take no time. All-day entries that are not free take
/// the whole local day.</para>
///
/// <para><b>Fail closed.</b> A calendar that could not be read leaves the result
/// <see cref="IsComplete"/> false, and an incomplete result says nothing is free:
/// <see cref="IsFree"/> is false and <see cref="FreeGaps"/> is empty. A partner's calendar that is not
/// shared yet must read as "cannot place anything for her", never "she has nothing on".</para>
///
/// <para><b>Times.</b> Every interval is a half-open <c>[Start, End)</c> of <see cref="DateTimeOffset"/>
/// carrying the household zone's own offset, clipped to the window <see cref="WindowStart"/> to
/// <see cref="WindowEnd"/> (the local days asked for, 23 or 25 hours long across a clock change). An
/// interval that crosses midnight inside the window is one interval, not two. Overlapping or touching
/// intervals merge into one that keeps every reason.</para>
///
/// <para><b>What this does not know:</b> sleep. The model protects what the PBI names; the hours
/// between bedtime and the morning are "free" here, and the planner bounds its own search to waking
/// hours.</para>
/// </summary>
public sealed class BusyTime
{
    /// <summary>The longest run of days one answer covers.</summary>
    public const int MaxDays = 366;

    private static readonly TimeSpan PointEntryLength = TimeSpan.FromMinutes(1);

    private BusyTime()
    {
    }

    /// <summary>The adult, by the name the rules know them by.</summary>
    public string Person { get; private init; } = string.Empty;

    /// <summary>The first local moment of the first day asked for.</summary>
    public DateTimeOffset WindowStart { get; private init; }

    /// <summary>The first local moment after the last day asked for.</summary>
    public DateTimeOffset WindowEnd { get; private init; }

    /// <summary>What calendars hold, merged and in time order.</summary>
    public IReadOnlyList<BusyInterval> Busy { get; private init; } = [];

    /// <summary>What the household's rules keep clear, merged and in time order.</summary>
    public IReadOnlyList<BusyInterval> NeverPlaced { get; private init; } = [];

    /// <summary>Both together, merged and in time order; what a gap is the absence of.</summary>
    public IReadOnlyList<BusyInterval> Blocked { get; private init; } = [];

    /// <summary>False when any calendar that counts could not be read.</summary>
    public bool IsComplete => Unreadable.Count == 0;

    /// <summary>The calendars that could not be read, in the order own, family, children.</summary>
    public IReadOnlyList<UnreadableCalendar> Unreadable { get; private init; } = [];

    /// <summary>True when the work schedule was unknown and weekday hours were assumed. The head should ask for the real one.</summary>
    public bool WorkHoursAssumed { get; private init; }

    /// <summary>One day. See <see cref="For"/>.</summary>
    public static BusyTime ForDay(string person, DateOnly day, PlanningRules rules, IHouseholdCalendars calendars) =>
        For(person, day, day, rules, calendars);

    /// <summary>
    /// The busy and never-placed time of <paramref name="person"/> over the local days
    /// <paramref name="firstDay"/> to <paramref name="lastDay"/> inclusive (at most <see cref="MaxDays"/>).
    /// Reads each calendar once, for the whole window.
    /// </summary>
    /// <exception cref="ArgumentException">The person is not one of the rules' adults, or the rules hold no adult or two of one name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The days run backwards, or for more than <see cref="MaxDays"/>.</exception>
    public static BusyTime For(string person, DateOnly firstDay, DateOnly lastDay, PlanningRules rules, IHouseholdCalendars calendars)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(calendars);
        CheckRules(rules);
        var adult = rules.FindAdult(person)
            ?? throw new ArgumentException("The person asked for is not one of the household's adults.", nameof(person));
        if (lastDay < firstDay)
        {
            throw new ArgumentOutOfRangeException(nameof(lastDay), lastDay, "The last day is before the first day.");
        }

        if (lastDay.DayNumber - firstDay.DayNumber + 1 > MaxDays)
        {
            throw new ArgumentOutOfRangeException(nameof(lastDay), lastDay, $"One answer covers at most {MaxDays} days.");
        }

        var zone = rules.Zone;
        var windowStart = ZoneClock.StartOfDay(firstDay, zone);
        var windowEnd = ZoneClock.StartOfDay(lastDay.AddDays(1), zone);

        var own = Read(calendars.OwnCalendar(adult.Name, windowStart, windowEnd), "the person's own calendar");
        var family = Read(calendars.FamilyCalendar(windowStart, windowEnd), "the family calendar");
        var children = Read(calendars.ChildrensCalendars(windowStart, windowEnd), "the children's calendars");

        var unreadable = new List<UnreadableCalendar>();
        AddIfUnreadable(unreadable, CalendarSource.Own, own);
        AddIfUnreadable(unreadable, CalendarSource.Family, family);
        AddIfUnreadable(unreadable, CalendarSource.Children, children);

        var busy = new List<BusyInterval>();
        AddEntries(busy, own.Entries, BusyReason.OwnCalendar, zone);
        AddEntries(busy, family.Entries, BusyReason.FamilyCalendar, zone);
        AddEntries(busy, children.Entries.Where(entry => BlocksAdult(entry, adult, rules.Adults)), BusyReason.ChildrensEvent, zone);

        var never = NeverPlacedTime.For(adult.Name, firstDay, lastDay, rules, own.Entries, family.Entries);

        var busyInWindow = Window(busy, windowStart, windowEnd);
        var neverInWindow = Window(never, windowStart, windowEnd);
        return new BusyTime
        {
            Person = adult.Name,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            Busy = busyInWindow.AsReadOnly(),
            NeverPlaced = neverInWindow.AsReadOnly(),
            Blocked = Intervals.Merge(busyInWindow.Concat(neverInWindow)).AsReadOnly(),
            Unreadable = unreadable.AsReadOnly(),
            WorkHoursAssumed = !rules.Work.IsKnown,
        };
    }

    /// <summary>
    /// True when <c>[start, end)</c> shares no time with anything busy or never-placed. Touching is free.
    /// False when the result is incomplete, and false when any of the time lies outside the window,
    /// because nothing was read for it.
    /// </summary>
    /// <exception cref="ArgumentException">The time to check is empty or runs backwards.</exception>
    public bool IsFree(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ArgumentException("A time to check has to end after it starts.", nameof(end));
        }

        if (!IsComplete || start < WindowStart || end > WindowEnd)
        {
            return false;
        }

        return !Blocked.Any(interval => interval.Overlaps(start, end));
    }

    /// <summary>
    /// The stretches of <c>[from, to)</c> (cut to the window) in which nothing is taken, each at least
    /// <paramref name="minimum"/> long, in time order. Empty when the result is incomplete.
    /// </summary>
    /// <exception cref="ArgumentException">The range is empty or runs backwards.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The minimum is not positive.</exception>
    public IReadOnlyList<FreeGap> FreeGaps(DateTimeOffset from, DateTimeOffset to, TimeSpan minimum)
    {
        if (to <= from)
        {
            throw new ArgumentException("A range to look for gaps in has to end after it starts.", nameof(to));
        }

        if (minimum <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), minimum, "A gap has to be some length.");
        }

        if (!IsComplete)
        {
            return [];
        }

        var low = from > WindowStart ? from : WindowStart;
        var high = to < WindowEnd ? to : WindowEnd;
        var gaps = new List<FreeGap>();
        var cursor = low;
        foreach (var interval in Blocked)
        {
            if (interval.End <= cursor)
            {
                continue;
            }

            if (interval.Start >= high)
            {
                break;
            }

            if (interval.Start > cursor)
            {
                AddGap(gaps, cursor, interval.Start, minimum);
            }

            cursor = interval.End > cursor ? interval.End : cursor;
        }

        if (cursor < high)
        {
            AddGap(gaps, cursor, high, minimum);
        }

        return gaps;
    }

    private static void CheckRules(PlanningRules rules)
    {
        if (rules.Adults is null || rules.Adults.Count == 0)
        {
            throw new ArgumentException("The household rules name no adults, so there is no one to plan for.", nameof(rules));
        }

        var duplicate = rules.Adults
            .GroupBy(adult => adult.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException("Two adults in the household rules share a name, so they could not be told apart.", nameof(rules));
        }
    }

    private static CalendarRead Read(CalendarRead? read, string which) =>
        read ?? throw new InvalidOperationException($"The calendar source returned nothing for {which}; it must return a read or say why it is unreadable.");

    private static void AddIfUnreadable(List<UnreadableCalendar> unreadable, CalendarSource source, CalendarRead read)
    {
        if (!read.IsReadable)
        {
            unreadable.Add(new UnreadableCalendar(source, read.UnreadableBecause!));
        }
    }

    // A children's event is this adult's when it names them; when it names no adult it is the
    // pickup adults'. Naming wins over the pickup flag (see the class summary).
    private static bool BlocksAdult(CalendarEntry entry, Adult adult, IReadOnlyList<Adult> adults)
    {
        var named = NamedAdults.In(entry, adults);
        return named.Count > 0
            ? named.Any(other => string.Equals(other.Name, adult.Name, StringComparison.OrdinalIgnoreCase))
            : adult.OnPickup;
    }

    private static void AddEntries(List<BusyInterval> into, IEnumerable<CalendarEntry> entries, BusyReason reason, TimeZoneInfo zone)
    {
        foreach (var entry in entries.Where(entry => entry.Availability == Availability.Busy))
        {
            if (entry.IsAllDay)
            {
                into.Add(new BusyInterval(
                    ZoneClock.StartOfDay(entry.FirstDay, zone),
                    ZoneClock.StartOfDay(entry.EndDayExclusive, zone),
                    reason));
            }
            else
            {
                // A point in time has nothing to overlap, but a session that spans it is on top of it.
                var end = entry.End > entry.Start ? entry.End : entry.Start + PointEntryLength;
                into.Add(new BusyInterval(ZoneClock.InZone(entry.Start, zone), ZoneClock.InZone(end, zone), reason));
            }
        }
    }

    private static List<BusyInterval> Window(IEnumerable<BusyInterval> intervals, DateTimeOffset from, DateTimeOffset to) =>
        Intervals.Merge(intervals.Select(interval => Intervals.Clip(interval, from, to)).OfType<BusyInterval>());

    private static void AddGap(List<FreeGap> gaps, DateTimeOffset start, DateTimeOffset end, TimeSpan minimum)
    {
        if (end - start >= minimum)
        {
            gaps.Add(new FreeGap(start, end));
        }
    }
}
