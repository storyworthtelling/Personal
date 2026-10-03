using System.Globalization;
using Index.Domain.Household;

namespace Index.Domain.Voice;

/// <summary>
/// The facts a seat may be told (remediation 3.7), computed from real data and reduced before they leave:
/// percentages and counts spoken in words, a month's name, and — on Savings alone — one rounded monthly
/// rate. What is NOT passed, by construction of these signatures and not by care: any balance, any total
/// owed or saved, a goal, a bank or account name, a mask. The same builders serve both installs; the
/// owner's pages convert their live series to the household file's shape first
/// (<c>HouseholdChartSeries.FromSnowballHistory</c>), so there is one set of rules, not two that could drift.
///
/// <para>Pure and database-free. No facts (too little history, damaged data, nothing honest to say) is an
/// empty <see cref="VoiceFacts"/>, and the seat is then hidden.</para>
///
/// <para><b>The rules, stated once.</b> The total owed in a week is the sum of every loan's balance that
/// week. <i>Snowball</i>: the percent the latest total is below the highest total ever recorded (and the
/// month of that peak, relative to the latest week), when that rounds to at least one. <i>Accounts</i>: the
/// percent change since the anchor, which is the earliest recorded week on or after the first of the month
/// before the latest week's month — "since August" on a September page — and only when the anchor is at
/// least <see cref="MinimumAnchorDays"/> days back. Percentages round to a whole number away from zero, so
/// 0.9% is "one percent"; a change that rounds to nothing is "about the same". Both seats count the loans
/// already paid off. <i>Savings</i>: the sum of the monthly paces of the accounts that have any history,
/// rounded to a figure a person would say (<see cref="RoundedRate"/>), only when positive.</para>
/// </summary>
public static class VoiceFactBuilder
{
    /// <summary>An anchor closer than this to the latest week is a fortnight's wobble, not a trend.</summary>
    public const int MinimumAnchorDays = 28;

    // ---- Snowball ----

    public static VoiceFacts Snowball(HouseholdSnowballHistory? history)
    {
        var lines = new List<string>();
        var totals = Totals(history);
        if (totals is not null)
        {
            var peak = totals.OrderByDescending(week => week.Total).ThenBy(week => week.Week).First();
            var latest = totals[^1];
            if (peak.Week != latest.Week && peak.Total > 0m)
            {
                var percent = WholePercent((peak.Total - latest.Total) / peak.Total);
                if (percent >= 1)
                {
                    lines.Add(
                        $"Total owed is down {NumberWords.Of(percent)} percent from its peak, which was " +
                        $"{PeakPhrase(peak.Week, latest.Week)}.");
                }
            }
        }

        AddPaidOff(lines, history);
        return new VoiceFacts(VoiceSeat.Snowball, lines);
    }

    // ---- Accounts ----

    public static VoiceFacts Accounts(HouseholdSnowballHistory? history)
    {
        var lines = new List<string>();
        var totals = Totals(history);
        if (totals is not null)
        {
            var latest = totals[^1];
            var firstOfAnchorMonth = new DateOnly(latest.Week.Year, latest.Week.Month, 1).AddMonths(-1);
            var anchor = totals.Where(week => week.Week >= firstOfAnchorMonth).OrderBy(week => week.Week).First();

            if (latest.Week.DayNumber - anchor.Week.DayNumber >= MinimumAnchorDays && anchor.Total > 0m)
            {
                var month = MonthName(anchor.Week);
                var change = (latest.Total - anchor.Total) / anchor.Total;
                var percent = WholePercent(Math.Abs(change));
                lines.Add(percent == 0
                    ? $"Total owed is about the same as it was in {month}."
                    : $"Total owed is {(change < 0m ? "down" : "up")} {NumberWords.Of(percent)} percent since {month}.");
            }
        }

        AddPaidOff(lines, history);
        return new VoiceFacts(VoiceSeat.Accounts, lines);
    }

    // ---- Savings ----

    /// <summary>
    /// The one seat allowed a dollar figure, and then only a RATE: the savings accounts' combined monthly
    /// pace. The accounts arrive with their balances, goals and names (that is the household file's shape)
    /// and only <c>MonthlyPace</c> and <c>MonthsOfHistory</c> are read.
    /// </summary>
    public static VoiceFacts Savings(IReadOnlyList<HouseholdSavingsAccount> accounts)
    {
        var combined = accounts.Where(account => account.MonthsOfHistory > 0).Sum(account => account.MonthlyPace);
        var rate = RoundedRate(combined);
        return rate <= 0
            ? VoiceFacts.None(VoiceSeat.Savings)
            : new VoiceFacts(VoiceSeat.Savings,
                [$"Taken together, the savings accounts are growing by about {rate.ToString(CultureInfo.InvariantCulture)} dollars a month."]);
    }

    /// <summary>
    /// A pace as a figure a person would say aloud ("three hundred a month", not 187.50): to the nearest
    /// five under a hundred, ten under a thousand, a hundred above. Zero or below, or too small to round to
    /// five, is 0 — nothing worth saying.
    /// </summary>
    public static int RoundedRate(decimal monthlyPace)
    {
        if (monthlyPace <= 0m)
        {
            return 0;
        }

        var step = monthlyPace >= 1000m ? 100m : monthlyPace >= 100m ? 10m : 5m;
        return (int)(Math.Round(monthlyPace / step, MidpointRounding.AwayFromZero) * step);
    }

    // ---- shared ----

    /// <summary>A fraction (0.009 = 0.9%) as a whole percent, halves away from zero: 0.9% is one, 0.5% is
    /// one, 0.4% is zero.</summary>
    public static int WholePercent(decimal fraction) =>
        (int)Math.Round(fraction * 100m, MidpointRounding.AwayFromZero);

    private static void AddPaidOff(List<string> lines, HouseholdSnowballHistory? history)
    {
        var paidOff = history?.LoansInFocusOrder?.Count(loan => loan.IsPaidOff) ?? 0;
        if (paidOff > 0 && Totals(history) is not null)
        {
            lines.Add(paidOff == 1
                ? "One loan is already paid off."
                : $"{NumberWords.Capitalised(paidOff)} loans are already paid off.");
        }
    }

    /// <summary>The total owed each recorded week, oldest first, or null when there is nothing honest to
    /// read: no history, fewer than two weeks, or a series whose arrays do not line up (a damaged file is
    /// refused, never read wrong).</summary>
    private static List<(DateOnly Week, decimal Total)>? Totals(HouseholdSnowballHistory? history)
    {
        if (history?.Weeks is null || history.LoansInFocusOrder is null
            || history.Weeks.Count < 2 || history.LoansInFocusOrder.Count == 0
            || history.LoansInFocusOrder.Any(loan => loan.Balances is null || loan.Balances.Count != history.Weeks.Count))
        {
            return null;
        }

        return history.Weeks
            .Select((week, i) => (Week: week, Total: history.LoansInFocusOrder.Sum(loan => loan.Balances[i])))
            .OrderBy(week => week.Week)
            .ToList();
    }

    private static string MonthName(DateOnly day) => day.ToString("MMMM", CultureInfo.InvariantCulture);

    // "last November", "in February", "in November, more than a year ago" — never a year in digits.
    private static string PeakPhrase(DateOnly peak, DateOnly latest) => (latest.Year - peak.Year) switch
    {
        0 => $"in {MonthName(peak)}",
        1 => $"last {MonthName(peak)}",
        _ => $"in {MonthName(peak)}, more than a year ago",
    };
}
