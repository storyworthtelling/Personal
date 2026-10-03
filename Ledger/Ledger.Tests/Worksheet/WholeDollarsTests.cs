using Index.Domain.Worksheet;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Guard G19 (D154): every printed figure is floored to whole dollars (a payment is rounded UP), never
/// rounded to nearest, never with cents — and a printed TOTAL must be the sum of the printed, already-floored parts, never a
/// total floored separately from the unrounded numbers (which can disagree by a dollar). Watched to
/// fail first against exactly that bug: flooring the combined raw sum instead of summing the floors.
/// </summary>
public class WholeDollarsTests
{
    [Theory]
    [InlineData(548.99, 548)]
    [InlineData(548.01, 548)]
    [InlineData(0.99, 0)]
    [InlineData(1000, 1000)]
    public void FloorsDown_NeverToNearest(decimal amount, long expected)
    {
        Assert.Equal(expected, WholeDollars.Floor(amount));
    }

    [Theory]
    [InlineData(548.00, 548)]
    [InlineData(548.01, 549)]
    [InlineData(548.50, 549)]
    [InlineData(548.99, 549)]
    [InlineData(0, 0)]
    public void ACeilingRoundsUpToTheNextWholeDollar(decimal amount, long expected)
    {
        Assert.Equal(expected, WholeDollars.Ceiling(amount));
    }

    [Fact]
    public void ANegativeCeilingGoesTowardPositiveInfinity()
    {
        Assert.Equal(-1, WholeDollars.Ceiling(-1.5m));
        Assert.Equal(0, WholeDollars.Ceiling(-0.99m));
    }

    [Fact]
    public void NegativeCentsStillFloorDown_TowardZeroIsWrong()
    {
        // Math.Floor(-1.5) = -2, not -1 — flooring is DOWN in absolute terms too, not "toward zero".
        Assert.Equal(-2, WholeDollars.Floor(-1.5m));
    }

    [Fact]
    public void ReintroducedBug_FlooringTheCombinedTotal_DisagreesWithSummingTheFlooredParts()
    {
        // Three parts, each with a .50 fractional dollar: their RAW sum's fractional part carries a
        // whole dollar the individual floors each throw away — the exact case a naive "print the parts,
        // then floor their sum" composer gets wrong.
        decimal[] parts = [10.50m, 5.50m, 2.50m];

        var flooredRawSum = WholeDollars.Floor(parts.Sum());
        var sumOfFlooredParts = WholeDollars.SumOfFlooredParts(parts);

        Assert.Equal(18, flooredRawSum);       // 18.50 floored
        Assert.Equal(17, sumOfFlooredParts);   // 10 + 5 + 2 — the correct, printed-consistent answer
        Assert.NotEqual(flooredRawSum, sumOfFlooredParts);
    }

    [Fact]
    public void LeftToAssignIsBuiltFromTheAlreadyRoundedPrintedPartsNeverRoundedSeparately()
    {
        // Chosen so the raw (unrounded) combination would floor to a DIFFERENT number than combining
        // the three lines' own already-printed figures: income and last month floor, payments round UP.
        var result = LeftToAssignCalculator.Calculate(
            projectedMonthlyIncome: 9_350.90m,
            weeklyPaymentTotal: 1_250.10m,
            fridayCount: 5,
            lastMonthSpending: 1_906.10m);

        // Printed lines: income 9,350 ; payments ceiling(1250.10*5=6250.50)=6251 ; last month 1,906.
        Assert.Equal(9_350, result.IncomeWholeDollars);
        Assert.Equal(6_251, result.PaymentsWholeDollars);
        Assert.Equal(1_906, result.LastMonthSpendingWholeDollars);

        // Left to assign is EXACTLY the integer combination of the three printed parts above —
        // 9,350 - 6,251 - 1,906 = 1,193 — never a separate rounding of the raw arithmetic.
        Assert.Equal(9_350 - 6_251 - 1_906, result.LeftToAssignWholeDollars);

        var rawCombination = 9_350.90m - (1_250.10m * 5) - 1_906.10m; // = 1,194.30 -> floors to 1,194
        Assert.NotEqual(WholeDollars.Floor(rawCombination), result.LeftToAssignWholeDollars); // 1,194 != 1,193
    }

    [Fact]
    public void TheMonthsSnowballPaymentsRoundUpWhileIncomeAndSpendingStillFloor()
    {
        var result = LeftToAssignCalculator.Calculate(
            projectedMonthlyIncome: 9_350.99m,
            weeklyPaymentTotal: 285.42m,
            fridayCount: 5,
            lastMonthSpending: 1_906.99m);

        Assert.Equal(9_350, result.IncomeWholeDollars);
        Assert.Equal(1_428, result.PaymentsWholeDollars); // 285.42 x 5 = 1,427.10 -> 1,428
        Assert.Equal(1_906, result.LastMonthSpendingWholeDollars);
    }
}
