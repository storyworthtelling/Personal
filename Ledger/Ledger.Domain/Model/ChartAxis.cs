using System.Globalization;

namespace Index.Domain.Model;

/// <summary>A label on the time axis: the index of the week it sits under, and its text.</summary>
public readonly record struct MonthTick(int WeekIndex, string Label);

/// <summary>
/// The History charts' axes as pure arithmetic and pure text (mockup 6): round money steps for the
/// stacked bars' gridlines ("100k / 200k / 300k"), and calendar-month labels ("Jul '25 / Jan '26 /
/// Jul '26") at the weeks the months actually begin in.
/// </summary>
public static class ChartAxis
{
    /// <summary>The most gridlines the stacked chart draws; more than this is noise on a 150dp plot.</summary>
    public const int MaxTicks = 4;

    /// <summary>Round values (1, 2 or 5 × a power of ten, in whole dollars) from the first step up to
    /// the largest not above <paramref name="max"/>, using the smallest step that needs no more than
    /// <paramref name="maxTicks"/> lines. a six-figure total such as 280,842 gives 100k / 200k.</summary>
    public static IReadOnlyList<decimal> NiceSteps(decimal max, int maxTicks = MaxTicks)
    {
        if (max < 1m || maxTicks < 1)
        {
            return [];
        }

        var power = 1m;
        while (power * 10m <= max)
        {
            power *= 10m;
        }

        // Start a decade below the maximum's own, so a step can be smaller than it.
        for (var unit = Math.Max(power / 10m, 1m); ; unit *= 10m)
        {
            foreach (var multiple in new[] { 1m, 2m, 5m })
            {
                var step = unit * multiple;
                if (Math.Floor(max / step) <= maxTicks)
                {
                    return Enumerable.Range(1, (int)Math.Floor(max / step)).Select(n => n * step).ToList();
                }
            }
        }
    }

    /// <summary>A gridline's label: <c>100k</c>, <c>2.5k</c>, <c>1.5M</c>, or plain dollars under a thousand.</summary>
    public static string MoneyTick(decimal value)
    {
        var invariant = CultureInfo.InvariantCulture;
        return value >= 1_000_000m
            ? (value / 1_000_000m).ToString("0.##", invariant) + "M"
            : value >= 1_000m
                ? (value / 1_000m).ToString("0.##", invariant) + "k"
                : value.ToString("0", invariant);
    }

    /// <summary>"Jul '25". Not <c>ToString("MMM yy")</c>, which prints "Jul 25"; and not
    /// <c>"MMM ''yy"</c>, because in a .NET custom format a quote delimits a literal, so <c>''</c> is an
    /// empty literal and still prints no apostrophe (<c>SnowballChartMathTests</c> proves both). Invariant
    /// culture, so the device's language never changes the month's name.</summary>
    public static string MonthYearLabel(DateOnly date) =>
        date.ToString("MMM", CultureInfo.InvariantCulture) + " '" + date.ToString("yy", CultureInfo.InvariantCulture);

    /// <summary>Labels at calendar-month boundaries — January and July by default (every six months) —
    /// each under the week that CONTAINS the first of the month (the last recorded week on or before
    /// it), which is where the mockup puts them. A boundary before the first week or past the last is
    /// left out; a range that holds none gets its first week labelled so the axis is never bare.</summary>
    public static IReadOnlyList<MonthTick> MonthTicks(IReadOnlyList<DateOnly> weeks, int everyMonths = 6)
    {
        if (weeks.Count == 0)
        {
            return [];
        }

        var ticks = new List<MonthTick>();
        var first = weeks[0];
        var last = weeks[^1];
        var month = new DateOnly(first.Year, first.Month, 1).AddMonths(1);

        for (; month <= last.AddDays(6); month = month.AddMonths(1))
        {
            if ((month.Month - 1) % everyMonths != 0 || month <= first)
            {
                continue;
            }

            var index = IndexOfWeekContaining(weeks, month);
            if (ticks.Count == 0 || ticks[^1].WeekIndex != index)
            {
                ticks.Add(new MonthTick(index, MonthYearLabel(month)));
            }
        }

        return ticks.Count > 0 ? ticks : [new MonthTick(0, MonthYearLabel(first))];
    }

    private static int IndexOfWeekContaining(IReadOnlyList<DateOnly> weeks, DateOnly day)
    {
        var index = 0;
        for (var i = 0; i < weeks.Count && weeks[i] <= day; i++)
        {
            index = i;
        }
        return index;
    }
}
