using Constitution.Domain.Calendar;

namespace Constitution.Domain.Placement;

/// <summary>A half-open stretch of time <c>[Start, End)</c>.</summary>
internal readonly record struct Stretch(DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Length => End - Start;
}

/// <summary>
/// The time already spoken for during one placement pass, person by person, on top of what each person's
/// <see cref="BusyTime"/> already blocks. Everything placed (and everything the owner has left on the calendar)
/// is claimed here, so a later event can only be given what is really free. It answers one question: when is
/// everyone in this list free inside this stretch.
/// </summary>
internal sealed class PlacementLedger
{
    private readonly Dictionary<string, BusyTime> busy;
    private readonly Dictionary<string, List<BusyInterval>> claims = new(StringComparer.OrdinalIgnoreCase);

    public PlacementLedger(IEnumerable<BusyTime> people)
    {
        busy = people.ToDictionary(person => person.Person, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Takes <c>[start, end)</c> from <paramref name="person"/>. Touching claims are fine; nothing is refused here.</summary>
    public void Claim(string person, DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            return;
        }

        if (!claims.TryGetValue(person, out var list))
        {
            list = [];
            claims[person] = list;
        }

        list.Add(new BusyInterval(start, end, BusyReason.OwnCalendar));
    }

    /// <summary>
    /// The stretches of <paramref name="region"/> in which every one of <paramref name="people"/> is free of
    /// busy time, never-placed time and earlier claims, in time order. Time outside a person's busy-time window
    /// was never read for them, so it is not free.
    /// </summary>
    public List<Stretch> CommonFree(IReadOnlyList<string> people, Stretch region)
    {
        List<Stretch>? common = null;
        foreach (var person in people)
        {
            var free = FreeFor(person, region);
            common = common is null ? free : Intersect(common, free);
        }

        return common ?? [];
    }

    private List<Stretch> FreeFor(string person, Stretch region)
    {
        var time = busy[person];
        var low = region.Start > time.WindowStart ? region.Start : time.WindowStart;
        var high = region.End < time.WindowEnd ? region.End : time.WindowEnd;
        var free = new List<Stretch>();
        if (high <= low)
        {
            return free;
        }

        var blocked = Intervals.Merge(time.Blocked.Concat(claims.GetValueOrDefault(person) ?? []));
        var cursor = low;
        foreach (var interval in blocked)
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
                free.Add(new Stretch(cursor, interval.Start));
            }

            cursor = interval.End > cursor ? interval.End : cursor;
        }

        if (cursor < high)
        {
            free.Add(new Stretch(cursor, high));
        }

        return free;
    }

    private static List<Stretch> Intersect(List<Stretch> left, List<Stretch> right)
    {
        var both = new List<Stretch>();
        foreach (var a in left)
        {
            foreach (var b in right)
            {
                var start = a.Start > b.Start ? a.Start : b.Start;
                var end = a.End < b.End ? a.End : b.End;
                if (end > start)
                {
                    both.Add(new Stretch(start, end));
                }
            }
        }

        return both.OrderBy(span => span.Start).ToList();
    }
}
