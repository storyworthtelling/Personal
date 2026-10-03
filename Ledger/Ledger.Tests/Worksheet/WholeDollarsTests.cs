using Index.Domain.Worksheet;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Guard G19 (D154): every printed figure is floored to whole dollars, never rounded to nearest, never
/// with cents — and a printed TOTAL must be the sum of the printed, already-floored parts, never a
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
    public void LeftToAssign_IsBuiltFromAlreadyFlooredPrintedParts_NeverFlooredSeparately()
    {
        // Chosen so the raw (unfloored) combination would floor to a DIFFERENT number than combining
        // the three lines' own already-printed, already-floored figures.
        var result = LeftToAssignCalculator.Calculate(
            projectedMonthlyIncome: 9_350.90m,
            weeklyPaymentTotal: 1_250.70m,
            fridayCount: 5,
            lastMonthSpending: 1_906.90m);

        // Printed lines: income 9,350 ; payments floor(1250.70*5=6253.50)=6253 ; last month 1,906.
        Assert.Equal(9_350, result.IncomeWholeDollars);
        Assert.Equal(6_253, result.PaymentsWholeDollars);
        Assert.Equal(1_906, result.LastMonthSpendingWholeDollars);

        // Left to assign is EXACTLY the integer combination of the three printed parts above —
        // 9,350 - 6,253 - 1,906 = 1,191 — never a separate floor of the raw arithmetic.
        Assert.Equal(9_350 - 6_253 - 1_906, result.LeftToAssignWholeDollars);

        var rawCombination = 9_350.90m - (1_250.70m * 5) - 1_906.90m; // = 1,190.50 -> floors to 1,190
        Assert.NotEqual(WholeDollars.Floor(rawCombination), result.LeftToAssignWholeDollars); // 1,190 != 1,191
    }
}
