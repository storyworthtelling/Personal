using Constitution.Domain.Calendar;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Calendar;

/// <summary>
/// The interval arithmetic behind C-5.3.1 checked against a second, deliberately dumb
/// implementation. For a few hundred seeded random weeks (a week that contains the fall-back
/// night, so a day is 25 hours long on one of them) the answer is compared with a grid of
/// minutes: each minute of the week is either taken or it is not, decided without merging,
/// clipping or complementing anything. <see cref="BusyTime.Blocked"/> must be exactly the runs of
/// taken minutes, and <see cref="BusyTime.IsFree"/> must say a time is free exactly when none of
/// its minutes is taken.
///
/// <para>The grid is built from what the generator <i>knows</i> about each entry (who it names, what
/// it covers), not from the code under test, so a bug in name matching, merging, clipping, all-day
/// ends or the clock change shows up as a disagreement rather than being agreed with. A failure
/// prints the seed and the round, which is everything needed to replay it.</para>
/// </summary>
public class BusyTimePropertyTests
{
    private const int Rounds = 250;
    private const int FirstSeed = 5300;

    // The week of Monday 26 October 2026 runs through the fall-back night to Monday 2 November at
    // midnight: 7 days and one extra hour.
    private static readonly DateOnly WeekStart = Monday;
    private static readonly DateTimeOffset GridStart = Edt(10, 26, 0);
    private static readonly int GridMinutes = (int)(Est(11, 2, 0) - GridStart).TotalMinutes;

    /// <summary>A title, who it truly names, and so who a children's event with that title belongs to.</summary>
    private sealed record Titled(string Title, string[] Names);

    private static readonly Titled[] ChildTitles =
    [
        new("Robin soccer, Lee drives", [Lee]),
        new("Juniper swimming", []),
        new("Samba class", []),
        new("Parent-teacher night: Sam and Lee", [Sam, Lee]),
        new("Sam's turn to take Robin", [Sam]),
        new("Robin at the Leeds museum", []),
    ];

    private static readonly string[] OrdinaryTitles = ["Dentist", "Call", "Plumber", "Recital", "Haircut"];

    [Fact]
    public void RandomWeeksAgreeWithAMinuteGrid()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var seed = FirstSeed + round;
            CheckWeek(new Random(seed), seed);
        }
    }

    private static void CheckWeek(Random random, int seed)
    {
        var samOnPickup = random.Next(2) == 0;
        var leeOnPickup = random.Next(4) != 0;

        // Entries, and for each the minutes it truly takes, by person.
        var samEntries = new List<CalendarEntry>();
        var leeEntries = new List<CalendarEntry>();
        var familyEntries = new List<CalendarEntry>();
        var childEntries = new List<CalendarEntry>();
        var sam = new bool[GridMinutes];
        var lee = new bool[GridMinutes];

        for (var i = random.Next(0, 5); i > 0; i--)
        {
            var (entry, minutes) = RandomEntry(random, OrdinaryTitles[random.Next(OrdinaryTitles.Length)]);
            samEntries.Add(entry);
            Mark(sam, minutes);
        }

        for (var i = random.Next(0, 5); i > 0; i--)
        {
            var (entry, minutes) = RandomEntry(random, OrdinaryTitles[random.Next(OrdinaryTitles.Length)]);
            leeEntries.Add(entry);
            Mark(lee, minutes);
        }

        for (var i = random.Next(0, 4); i > 0; i--)
        {
            var (entry, minutes) = RandomEntry(random, OrdinaryTitles[random.Next(OrdinaryTitles.Length)]);
            familyEntries.Add(entry);
            Mark(sam, minutes);
            Mark(lee, minutes);
        }

        for (var i = random.Next(0, 5); i > 0; i--)
        {
            var titled = ChildTitles[random.Next(ChildTitles.Length)];
            var (entry, minutes) = RandomEntry(random, titled.Title);
            childEntries.Add(entry);
            var blocksSam = titled.Names.Length > 0 ? titled.Names.Contains(Sam) : samOnPickup;
            var blocksLee = titled.Names.Length > 0 ? titled.Names.Contains(Lee) : leeOnPickup;
            if (blocksSam)
            {
                Mark(sam, minutes);
            }

            if (blocksLee)
            {
                Mark(lee, minutes);
            }
        }

        var calendars = Calendars(samEntries, leeEntries, familyEntries, childEntries);
        var rules = Rules(samOnPickup: samOnPickup, leeOnPickup: leeOnPickup);

        Check(Sam, sam, WorkMinutes(8, 0, 16, 0), rules, calendars, random, seed);
        Check(Lee, lee, WorkMinutes(8, 0, 15, 30), rules, calendars, random, seed);
    }

    private static void Check(string person, bool[] calendarMinutes, bool[] workMinutes, PlanningRules rules, HouseholdCalendarSnapshot calendars, Random random, int seed)
    {
        var week = BusyTime.For(person, WeekStart, WeekStart.AddDays(6), rules, calendars);
        var never = new bool[GridMinutes];
        for (var minute = 0; minute < GridMinutes; minute++)
        {
            never[minute] = workMinutes[minute] || Dinner[minute] || Routine[minute];
        }

        var where = $"person {person}, seed {seed}";
        Assert.True(week.IsComplete, where);
        AssertSameRuns(Runs(calendarMinutes), RunsOf(week.Busy), "busy", where);
        AssertSameRuns(Runs(never), RunsOf(week.NeverPlaced), "never-placed", where);

        var taken = new bool[GridMinutes];
        for (var minute = 0; minute < GridMinutes; minute++)
        {
            taken[minute] = calendarMinutes[minute] || never[minute];
        }

        AssertSameRuns(Runs(taken), RunsOf(week.Blocked), "blocked", where);

        for (var ask = 0; ask < 60; ask++)
        {
            var from = random.Next(0, GridMinutes - 1);
            var to = Math.Min(GridMinutes, from + 1 + random.Next(0, 600));
            var expected = !Enumerable.Range(from, to - from).Any(minute => taken[minute]);
            var actual = week.IsFree(GridStart.AddMinutes(from), GridStart.AddMinutes(to));
            Assert.True(expected == actual, $"IsFree({from}, {to}) was {actual}, the grid says {expected} ({where})");
        }
    }

    private static void AssertSameRuns(List<(int From, int To)> expected, List<(int From, int To)> actual, string which, string where)
    {
        if (expected.SequenceEqual(actual))
        {
            return;
        }

        var firstDifference = Enumerable.Range(0, Math.Max(expected.Count, actual.Count))
            .First(i => i >= expected.Count || i >= actual.Count || expected[i] != actual[i]);
        var said = firstDifference < actual.Count ? actual[firstDifference].ToString() : "nothing";
        var meant = firstDifference < expected.Count ? expected[firstDifference].ToString() : "nothing";
        Assert.Fail($"{which} runs differ at run {firstDifference}: the grid has {meant}, BusyTime said {said} ({where}; {expected.Count} runs expected, {actual.Count} given)");
    }

    // ---- the grid ----

    private static readonly bool[] Dinner = Daily(18, 0, 19, 0);
    private static readonly bool[] Routine = Daily(19, 15, 20, 0);

    private static void Mark(bool[] grid, (int From, int To) minutes)
    {
        for (var minute = Math.Max(0, minutes.From); minute < Math.Min(GridMinutes, minutes.To); minute++)
        {
            grid[minute] = true;
        }
    }

    /// <summary>The offset on a date for the clock times used here (none of them is in the repeated hour).</summary>
    private static TimeSpan OffsetAt(DateOnly date, int hour) =>
        date < new DateOnly(2026, 11, 1) || (date == new DateOnly(2026, 11, 1) && hour < 2)
            ? TimeSpan.FromHours(-4)
            : TimeSpan.FromHours(-5);

    private static int MinuteOf(DateOnly date, int hour, int minute)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0);
        var instant = new DateTimeOffset(local, OffsetAt(date, hour));
        return (int)(instant - GridStart).TotalMinutes;
    }

    private static bool[] Daily(int fromHour, int fromMinute, int toHour, int toMinute)
    {
        var grid = new bool[GridMinutes];
        for (var day = 0; day < 7; day++)
        {
            var date = WeekStart.AddDays(day);
            Mark(grid, (MinuteOf(date, fromHour, fromMinute), MinuteOf(date, toHour, toMinute)));
        }

        return grid;
    }

    private static bool[] WorkMinutes(int fromHour, int fromMinute, int toHour, int toMinute)
    {
        var grid = new bool[GridMinutes];
        for (var day = 0; day < 5; day++)
        {
            var date = WeekStart.AddDays(day);
            Mark(grid, (MinuteOf(date, fromHour, fromMinute), MinuteOf(date, toHour, toMinute)));
        }

        return grid;
    }

    private static List<(int From, int To)> Runs(bool[] grid)
    {
        var runs = new List<(int, int)>();
        var start = -1;
        for (var minute = 0; minute <= grid.Length; minute++)
        {
            var on = minute < grid.Length && grid[minute];
            if (on && start < 0)
            {
                start = minute;
            }
            else if (!on && start >= 0)
            {
                runs.Add((start, minute));
                start = -1;
            }
        }

        return runs;
    }

    private static List<(int From, int To)> RunsOf(IReadOnlyList<BusyInterval> intervals) =>
        intervals.Select(interval => (
            (int)(interval.Start - GridStart).TotalMinutes,
            (int)(interval.End - GridStart).TotalMinutes)).ToList();

    // ---- the generator ----

    /// <summary>
    /// A random entry and the grid minutes it truly takes. A third are all-day (their minutes are the
    /// zone's real local days, 24 hours or 25 on the fall-back day); the rest are timed, on the
    /// quarter hour, some crossing midnight and some beginning before the week. One in seven is marked
    /// free, and takes no minutes at all.
    /// </summary>
    private static (CalendarEntry Entry, (int From, int To) Minutes) RandomEntry(Random random, string title)
    {
        var availability = random.Next(7) == 0 ? Availability.Free : Availability.Busy;
        var takes = availability == Availability.Busy;
        if (random.Next(3) == 0)
        {
            var first = random.Next(0, 7);
            var last = random.Next(first, 7);
            var firstDay = WeekStart.AddDays(first);
            var endExclusive = WeekStart.AddDays(last + 1);
            var entry = CalendarEntry.AllDay(title, firstDay, endExclusive, availability);
            return (entry, takes ? (MinuteOf(firstDay, 0, 0), MinuteOf(endExclusive, 0, 0)) : (0, 0));
        }

        var startMinute = random.Next(-600, GridMinutes - 30) / 15 * 15;
        var length = 15 * random.Next(1, 41);
        var timed = CalendarEntry.Timed(title, GridStart.AddMinutes(startMinute), GridStart.AddMinutes(startMinute + length), availability);
        return (timed, takes ? (startMinute, startMinute + length) : (0, 0));
    }
}
