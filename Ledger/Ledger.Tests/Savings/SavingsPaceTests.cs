using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Savings;

/// <summary>
/// Guard G23 (INDEX-P11-PLAN.md §4): a flat or falling pace must never divide, throw, or land on a
/// garbage date — it must say "not at this pace". Watched red first against the naive implementation
/// left commented in <c>SavingsPace.ReachesIt</c> (a straight <c>remaining / monthlyPace</c> with no
/// guard): a zero pace threw <see cref="DivideByZeroException"/> (decimal division by zero throws,
/// unlike double) and a negative pace silently computed a negative month count that landed on a PAST
/// date instead of failing loudly — both are exactly the two failure modes below.
/// </summary>
public class SavingsPaceTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 25);

    [Fact]
    public void ZeroPace_WithMoneyStillToGo_IsNotAtThisPace_NeverThrows()
    {
        var result = SavingsPace.ReachesIt(currentBalance: 1_350m, targetAmount: 5_000m, monthlyPace: 0m, AsOf);

        Assert.True(result.IsNotAtThisPace);
        Assert.Null(result.Date);
    }

    [Fact]
    public void NegativePace_WithMoneyStillToGo_IsNotAtThisPace_NeverAGarbageDate()
    {
        var result = SavingsPace.ReachesIt(currentBalance: 1_350m, targetAmount: 5_000m, monthlyPace: -40m, AsOf);

        Assert.True(result.IsNotAtThisPace);
        Assert.Null(result.Date);
    }

    [Fact]
    public void PositivePace_ProjectsAMonthInTheFuture_RoundedUp()
    {
        // $6,800 remaining at $260/month needs 27 months (26.2 rounded up), never rounded down.
        var result = SavingsPace.ReachesIt(currentBalance: 8_200m, targetAmount: 15_000m, monthlyPace: 260m, AsOf);

        Assert.False(result.IsNotAtThisPace);
        Assert.Equal(AsOf.AddMonths(27), result.Date);
    }

    [Fact]
    public void AlreadyAtOrPastTheGoal_ReachesItThisMonth_RegardlessOfPace()
    {
        // Even a zero/negative pace must not say "not at this pace" once the goal is already met —
        // there is nothing left to project.
        var result = SavingsPace.ReachesIt(currentBalance: 15_000m, targetAmount: 15_000m, monthlyPace: 0m, AsOf);

        Assert.False(result.IsNotAtThisPace);
        Assert.Equal(AsOf, result.Date);
    }

    [Fact]
    public void ComputePace_UsesAtMostTheLastTwelveMonths_AndReportsHowMany()
    {
        var history = new List<SavingsSnapshot>();
        // 14 months of history, growing by $100/month over the whole span, but the last 13 points
        // (12 months apart) grow by $50/month — the window must use the recent, slower pace, not the
        // whole 14-month span's faster one.
        var start = new DateOnly(2025, 1, 1);
        for (var i = 0; i < 2; i++)
        {
            history.Add(new SavingsSnapshot { MonthOf = start.AddMonths(i), AccountId = Guid.Empty, Balance = 1_000m + (i * 100m) });
        }
        var afterFast = 1_000m + (1 * 100m);
        for (var i = 2; i < 14; i++)
        {
            history.Add(new SavingsSnapshot { MonthOf = start.AddMonths(i), AccountId = Guid.Empty, Balance = afterFast + ((i - 1) * 50m) });
        }

        var (pace, months) = SavingsPace.ComputePace(history);

        Assert.Equal(12, months);
        Assert.Equal(50m, pace); // the recent $50/month pace, not the earlier faster stretch
    }

    [Fact]
    public void ComputePace_WithLessThanAYear_UsesWhatItHas()
    {
        var history = new List<SavingsSnapshot>
        {
            new() { MonthOf = new DateOnly(2026, 6, 1), AccountId = Guid.Empty, Balance = 1_950m },
            new() { MonthOf = new DateOnly(2026, 7, 1), AccountId = Guid.Empty, Balance = 2_012m },
            new() { MonthOf = new DateOnly(2026, 8, 1), AccountId = Guid.Empty, Balance = 2_074m },
            new() { MonthOf = new DateOnly(2026, 9, 1), AccountId = Guid.Empty, Balance = 1_350m },
        };

        var (pace, months) = SavingsPace.ComputePace(history);

        Assert.Equal(3, months);
        Assert.Equal((1_350m - 1_950m) / 3, pace);
    }

    [Fact]
    public void ComputePace_WithFewerThanTwoSnapshots_IsFlat_NotAnException()
    {
        var (pace, months) = SavingsPace.ComputePace(
            [new SavingsSnapshot { MonthOf = new DateOnly(2026, 9, 1), AccountId = Guid.Empty, Balance = 1_350m }]);

        Assert.Equal(0m, pace);
        Assert.Equal(0, months);
    }
}
