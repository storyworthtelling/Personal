using Index.Domain.External;
using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guard G22 (D101–D104): income is inferred from recurring deposits and PROJECTED from the detected
/// pattern, never summed from whatever landed in one sampled calendar month (D102) — and the detector
/// says "not sure" rather than guessing when it can't tell (D103). This is, per the plan's own words,
/// "the largest unproven piece of the phase," so the suite below is adversarial rather than happy-path:
/// every case is something a naive "just sum the deposits" implementation would get wrong.
///
/// <para><b>The reintroduced bug this guard is built to catch</b> (watched failing before
/// <see cref="IncomeDetector"/> existed, per <see cref="CleanBiweekly_DoesNotInflateOnAThreePaycheckMonth"/>
/// below): a naive detector that sums whatever deposits fall inside one sampled calendar month reports
/// a biweekly earner's income as ~38% higher in any month that happens to contain three paychecks
/// instead of two, purely because of where the 14-day cycle lands against the calendar — nothing about
/// the household's actual pay changed. <see cref="NaiveCalendarMonthSum"/> below is that wrong
/// implementation, kept in the test file (never in production code) specifically so this test can show
/// it disagreeing with the real detector on the same data, rather than merely asserting a number.</para>
/// </summary>
public class IncomeProjectionTests
{
    private static DateTimeOffset D(int year, int month, int day) => new(new DateTime(year, month, day), TimeSpan.Zero);

    // Plaid signs a transaction positive for money LEAVING the account, negative for money coming IN
    // (PlaidTransaction's own doc comment; BudgetService.RefreshTransactionsAsync casts the same field
    // straight through for spending). A deposit is therefore built with a NEGATIVE Amount.
    private static PlaidTransaction Deposit(DateTimeOffset date, decimal amount, string name = "Deposit") =>
        new(Guid.NewGuid().ToString(), "acct-1", date, name, null, (double)(-amount), "USD", Pending: false);

    // ---- The central watched-red case: a three-paycheck month must not inflate the projection ----

    /// <summary>The naive implementation this guard exists to rule out: exactly what D102 forbids —
    /// summing whatever deposits happen to fall inside one calendar month. Kept here, not in production
    /// code, purely to demonstrate the bug it would reintroduce.</summary>
    private static decimal NaiveCalendarMonthSum(IReadOnlyList<PlaidTransaction> transactions, int year, int month) =>
        transactions
            .Where(t => t.Amount < 0 && t.Date.Year == year && t.Date.Month == month)
            .Sum(t => (decimal)(-t.Amount));

    [Fact]
    public void CleanBiweekly_DoesNotInflateOnAThreePaycheckMonth()
    {
        // A $2,400 biweekly paycheck starting 1 Jan 2026: 1, 15, 29 Jan land THREE times in January
        // alone (a real, unremarkable consequence of a 14-day cycle against a 31-day month), then
        // continues fortnightly through May — ten occurrences in all, comfortably past
        // IncomeDetector.MinOccurrencesForConfidence.
        var deposits = Enumerable.Range(0, 10)
            .Select(i => Deposit(D(2026, 1, 1).AddDays(i * 14), 2400m))
            .ToList();

        // The naive month-sum bug, reproduced: January contains three paychecks, so a naive detector
        // reports $7,200 for that month — 38% above the household's real, steady pay.
        var naiveJanuary = NaiveCalendarMonthSum(deposits, 2026, 1);
        Assert.Equal(7200m, naiveJanuary);

        var estimate = IncomeDetector.Detect(deposits);

        Assert.True(estimate.IsConfident);
        // The correct projection: $2,400 × 26 paychecks/year ÷ 12 months = $5,200 — independent of
        // which calendar month was sampled, because the detector is never given a month to sample.
        Assert.Equal(5200m, estimate.ProjectedMonthlyIncome);
        Assert.NotEqual(naiveJanuary, estimate.ProjectedMonthlyIncome);
        Assert.True(estimate.ProjectedMonthlyIncome < naiveJanuary);

        var stream = Assert.Single(estimate.Streams);
        Assert.Equal(IncomeCadence.Biweekly, stream.Cadence);
        Assert.Equal(10, stream.OccurrenceCount);
        Assert.Equal(2400m, stream.TypicalAmount);
    }

    [Fact]
    public void CleanBiweekly_AlsoDoesNotUnderstateATwoPaycheckMonth()
    {
        // The same cycle's OTHER side of D102: a two-paycheck month must not read as less than the
        // real steady income either. March 2026 (12, 26 Mar only) has just two.
        var deposits = Enumerable.Range(0, 10)
            .Select(i => Deposit(D(2026, 1, 1).AddDays(i * 14), 2400m))
            .ToList();

        var naiveMarch = NaiveCalendarMonthSum(deposits, 2026, 3);
        Assert.Equal(4800m, naiveMarch); // two paychecks, naively summed

        var estimate = IncomeDetector.Detect(deposits);

        Assert.Equal(5200m, estimate.ProjectedMonthlyIncome);
        Assert.True(estimate.ProjectedMonthlyIncome > naiveMarch);
    }

    // ---- A monthly salary: confident, straightforward ----

    [Fact]
    public void MonthlySalary_IsConfident_AndProjectsStraightThrough()
    {
        var deposits = Enumerable.Range(0, 6)
            .Select(i => Deposit(D(2026, 1, 1).AddMonths(i), 5000m))
            .ToList();

        var estimate = IncomeDetector.Detect(deposits);

        Assert.True(estimate.IsConfident);
        Assert.Equal(5000m, estimate.ProjectedMonthlyIncome);
        var stream = Assert.Single(estimate.Streams);
        Assert.Equal(IncomeCadence.Monthly, stream.Cadence);
    }

    // ---- Two concurrent streams: a paycheck plus a smaller recurring side income ----

    [Fact]
    public void TwoConcurrentIncomeStreams_AreBothDetected_AndSummed()
    {
        var paycheck = Enumerable.Range(0, 10).Select(i => Deposit(D(2026, 1, 1).AddDays(i * 14), 2400m, "Employer"));
        var sideIncome = Enumerable.Range(0, 6).Select(i => Deposit(D(2026, 1, 5).AddMonths(i), 500m, "Freelance"));
        var deposits = paycheck.Concat(sideIncome).OrderBy(t => t.Date).ToList();

        var estimate = IncomeDetector.Detect(deposits);

        Assert.True(estimate.IsConfident);
        Assert.Equal(2, estimate.Streams.Count);
        Assert.Contains(estimate.Streams, s => s.Cadence == IncomeCadence.Biweekly && s.TypicalAmount == 2400m);
        Assert.Contains(estimate.Streams, s => s.Cadence == IncomeCadence.Monthly && s.TypicalAmount == 500m);
        // 2,400 × 26/12 + 500 × 1 = 5,200 + 500
        Assert.Equal(5700m, estimate.ProjectedMonthlyIncome);
    }

    // ---- A single large one-off transfer must never be classified as income ----

    [Fact]
    public void SingleLargeOneOffTransfer_Alone_IsNotClassifiedAsIncome()
    {
        // A home sale, or a large gift — one deposit, nothing to compare it against.
        var deposits = new[] { Deposit(D(2026, 3, 10), 50000m, "Title Company") };

        var estimate = IncomeDetector.Detect(deposits);

        Assert.False(estimate.IsConfident);
        Assert.Null(estimate.ProjectedMonthlyIncome);
        Assert.Empty(estimate.Streams);
        Assert.NotNull(estimate.Reason);
    }

    [Fact]
    public void SingleLargeOneOffTransfer_AlongsideARealPaycheck_IsExcludedRatherThanCounted()
    {
        var paycheck = Enumerable.Range(0, 10).Select(i => Deposit(D(2026, 1, 1).AddDays(i * 14), 2400m));
        var gift = new[] { Deposit(D(2026, 2, 14), 9000m, "Gift") };
        var deposits = paycheck.Concat(gift).OrderBy(t => t.Date).ToList();

        var estimate = IncomeDetector.Detect(deposits);

        Assert.True(estimate.IsConfident);
        // Exactly one stream — the gift never joins the paycheck's cluster (it is nowhere near 8% of
        // $2,400) and never reaches MinOccurrencesForConfidence on its own, so it contributes nothing.
        var stream = Assert.Single(estimate.Streams);
        Assert.Equal(2400m, stream.TypicalAmount);
        Assert.Equal(5200m, estimate.ProjectedMonthlyIncome); // unchanged by the $9,000 gift
    }

    // ---- Irregular, gig-economy-style deposits: "not sure," never a fabricated number ----

    [Fact]
    public void IrregularDeposits_WithNoStableCadenceOrAmount_AreNotSure()
    {
        // Seven deposits, no two close enough in amount (each more than 8% from every other) to form
        // a cluster at all, so none can ever reach MinOccurrencesForConfidence.
        var amounts = new[] { 800m, 1200m, 600m, 950m, 300m, 1650m, 700m };
        var deposits = amounts
            .Select((amount, i) => Deposit(D(2026, 1, 1).AddDays(i * 23), amount, "Gig Client"))
            .ToList();

        var estimate = IncomeDetector.Detect(deposits);

        Assert.False(estimate.IsConfident);
        Assert.Null(estimate.ProjectedMonthlyIncome);
        Assert.Empty(estimate.Streams);
        Assert.NotNull(estimate.Reason);
    }

    [Fact]
    public void SameAmountButIrregularSpacing_IsNotSure()
    {
        // Same $1,000 amount every time (so it WOULD cluster on amount), but no consistent cadence —
        // 6, 40, 9, 55, 3 days apart. A detector that only checked amount, and not spacing, would
        // wrongly call this a stream.
        var offsets = new[] { 0, 6, 46, 55, 110 };
        var deposits = offsets.Select(o => Deposit(D(2026, 1, 1).AddDays(o), 1000m, "Client")).ToList();

        var estimate = IncomeDetector.Detect(deposits);

        Assert.False(estimate.IsConfident);
        Assert.Null(estimate.ProjectedMonthlyIncome);
        Assert.NotNull(estimate.Reason);
    }

    // ---- Too little history: even a lone, clean-looking deposit is "not sure" ----

    [Fact]
    public void OnlyOneObservedPaycheck_IsNotSure_DespiteLookingClean()
    {
        var deposits = new[] { Deposit(D(2026, 3, 6), 2400m, "Employer") };

        var estimate = IncomeDetector.Detect(deposits);

        Assert.False(estimate.IsConfident);
        Assert.Null(estimate.ProjectedMonthlyIncome);
        Assert.Empty(estimate.Streams);
    }

    [Fact]
    public void OnlyTwoObservedPaychecks_IsAlsoNotSure_OneIntervalIsNotAPattern()
    {
        // Two deposits give exactly one interval — nothing to compare it against, so it cannot be
        // told apart from a coincidence. This is the boundary MinOccurrencesForConfidence = 3 exists
        // to draw explicitly, one below the floor.
        var deposits = new[]
        {
            Deposit(D(2026, 3, 6), 2400m, "Employer"),
            Deposit(D(2026, 3, 20), 2400m, "Employer"),
        };

        var estimate = IncomeDetector.Detect(deposits);

        Assert.False(estimate.IsConfident);
        Assert.Null(estimate.ProjectedMonthlyIncome);
    }

    // ---- The amount-tolerance boundary itself (±8%), stated explicitly rather than left implicit ----

    [Fact]
    public void ADepositWithinEightPercent_JoinsTheCluster_AsOrdinaryPaycheckVariance()
    {
        var deposits = new[]
        {
            Deposit(D(2026, 1, 1), 2400m),
            Deposit(D(2026, 1, 15), 2400m),
            Deposit(D(2026, 1, 29), 2400m),
            Deposit(D(2026, 2, 12), 2400m * 1.07m), // +7%: inside the 8% band — a bit of overtime
        };

        var estimate = IncomeDetector.Detect(deposits);

        Assert.True(estimate.IsConfident);
        var stream = Assert.Single(estimate.Streams);
        Assert.Equal(4, stream.OccurrenceCount);
    }

    [Fact]
    public void ADepositBeyondEightPercent_NeverJoinsTheCluster_AndIsExcludedOnItsOwn()
    {
        var deposits = new[]
        {
            Deposit(D(2026, 1, 1), 2400m),
            Deposit(D(2026, 1, 15), 2400m),
            Deposit(D(2026, 1, 29), 2400m),
            Deposit(D(2026, 2, 12), 2400m * 1.10m), // +10%: outside the 8% band
        };

        var estimate = IncomeDetector.Detect(deposits);

        Assert.True(estimate.IsConfident); // the first three still form a confident biweekly stream
        var stream = Assert.Single(estimate.Streams);
        Assert.Equal(3, stream.OccurrenceCount);
        Assert.Equal(2400m, stream.TypicalAmount); // the outlier never pulls the average up
    }

    // ---- Cadence coverage: weekly and semimonthly, alongside the biweekly/monthly cases above ----

    [Fact]
    public void WeeklySideIncome_IsDetected()
    {
        var deposits = Enumerable.Range(0, 8).Select(i => Deposit(D(2026, 1, 2).AddDays(i * 7), 300m)).ToList();

        var estimate = IncomeDetector.Detect(deposits);

        Assert.True(estimate.IsConfident);
        var stream = Assert.Single(estimate.Streams);
        Assert.Equal(IncomeCadence.Weekly, stream.Cadence);
        Assert.Equal(300m * 52 / 12, estimate.ProjectedMonthlyIncome);
    }

    [Fact]
    public void SemimonthlyPayroll_OnFixedCalendarDays_IsDetectedAsSemimonthly_NotBiweekly()
    {
        // Paid on the 1st and the 15th every month — the classic semimonthly pattern. Its intervals
        // (14 or 17 days depending on month length) drift across weekdays, which is exactly what tells
        // it apart from a true every-14-days biweekly cycle.
        var months = Enumerable.Range(0, 4).ToList(); // Jan–Apr 2026
        var deposits = months
            .SelectMany(m => new[] { D(2026, 1, 1).AddMonths(m), D(2026, 1, 15).AddMonths(m) })
            .Select(date => Deposit(date, 1800m))
            .OrderBy(t => t.Date)
            .ToList();

        var estimate = IncomeDetector.Detect(deposits);

        Assert.True(estimate.IsConfident);
        var stream = Assert.Single(estimate.Streams);
        Assert.Equal(IncomeCadence.Semimonthly, stream.Cadence);
        Assert.Equal(1800m * 2, estimate.ProjectedMonthlyIncome);
    }

    // ---- No deposits at all ----

    [Fact]
    public void NoDepositsWhatsoever_IsNotSure_NotAnException()
    {
        var estimate = IncomeDetector.Detect([]);

        Assert.False(estimate.IsConfident);
        Assert.Null(estimate.ProjectedMonthlyIncome);
        Assert.NotNull(estimate.Reason);
    }

    [Fact]
    public void OutflowsAreNeverTreatedAsDeposits()
    {
        // Plaid signs an outflow positive. A recurring $2,400 CHARGE (rent, say) must never be read as
        // income just because it repeats on a steady biweekly-looking cadence.
        var deposits = Enumerable.Range(0, 10)
            .Select(i => new PlaidTransaction(
                Guid.NewGuid().ToString(), "acct-1", D(2026, 1, 1).AddDays(i * 14), "Rent", null,
                2400d, "USD", Pending: false))
            .ToList();

        var estimate = IncomeDetector.Detect(deposits);

        Assert.False(estimate.IsConfident);
        Assert.Null(estimate.ProjectedMonthlyIncome);
    }
}
