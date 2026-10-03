using Index.Domain.Worksheet;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Guard G4 (INDEX-P11-PLAN.md, D100/D153): balanced means EXACTLY zero, in whole-dollar arithmetic —
/// never a tolerance band. Written first and watched to fail against exactly that substitution.
/// </summary>
public class ZeroBalanceCheckTests
{
    [Fact]
    public void ExactMatch_IsBalanced()
    {
        Assert.True(ZeroBalanceCheck.IsBalanced([548m, 196m, 162m, 2_194m], 3_100));
    }

    [Fact]
    public void OneDollarOver_IsNotBalanced()
    {
        Assert.False(ZeroBalanceCheck.IsBalanced([1_536m], 1_535));
        Assert.Equal(1, ZeroBalanceCheck.Difference([1_536m], 1_535));
    }

    [Fact]
    public void OneDollarUnder_IsNotBalanced()
    {
        Assert.False(ZeroBalanceCheck.IsBalanced([1_534m], 1_535));
        Assert.Equal(-1, ZeroBalanceCheck.Difference([1_534m], 1_535));
    }

    [Fact]
    public void MockupOverExample_IsThreeHundredAndThirtyThreeOver()
    {
        // Mockup 13(b): ten categories + three set-aside lines wrote $3,433 against a $3,100 target.
        decimal[] written =
        [
            690m, 284m, 162m, 174m, 228m, 286m, 68m, 85m, 248m, 158m, // categories
            690m, 225m, 135m, // set-aside: extra payment, two savings
        ];

        Assert.Equal(3_433, ZeroBalanceCheck.WrittenTotal(written));
        Assert.Equal(333, ZeroBalanceCheck.Difference(written, 3_100));
        Assert.False(ZeroBalanceCheck.IsBalanced(written, 3_100));
    }

    [Fact]
    public void MockupUnderExample_IsThreeHundredAndEightyNineUnassigned()
    {
        // Mockup 13(c): the same page wrote $2,711 against the same $3,100 target.
        decimal[] written =
        [
            565m, 192m, 162m, 174m, 222m, 286m, 68m, 85m, 64m, 43m, // categories
            490m, 225m, 135m, // set-aside
        ];

        Assert.Equal(2_711, ZeroBalanceCheck.WrittenTotal(written));
        Assert.Equal(-389, ZeroBalanceCheck.Difference(written, 3_100));
        Assert.False(ZeroBalanceCheck.IsBalanced(written, 3_100));
    }

    [Fact]
    public void SumOfSuggestions_FollowingOverSuggestions_ExactlyBalances()
    {
        // With the three suggested trims applied (Clothing 104, Groceries 548, Gifts 111) in place of
        // what was written, and every other line unchanged, the total is exactly the $3,100 target.
        decimal[] followed =
        [
            548m, 284m, 162m, 174m, 228m, 286m, 68m, 85m, 104m, 111m,
            690m, 225m, 135m,
        ];

        Assert.True(ZeroBalanceCheck.IsBalanced(followed, 3_100));
    }

    [Fact]
    public void SumOfSuggestions_FollowingUnderSuggestion_ExactlyBalances()
    {
        // With the extra payment raised to $879 (the suggested figure) and everything else unchanged.
        decimal[] followed =
        [
            565m, 192m, 162m, 174m, 222m, 286m, 68m, 85m, 64m, 43m,
            879m, 225m, 135m,
        ];

        Assert.True(ZeroBalanceCheck.IsBalanced(followed, 3_100));
    }

    [Fact]
    public void ReintroducedBug_AToleranceBand_WouldWronglyCallAnOffByOneDollarPageBalanced()
    {
        // The bug Guard G4 exists to catch: comparing with a small epsilon instead of exact equality.
        const long written = 1_536;
        const long target = 1_535;

        var naiveToleranceCheck = Math.Abs((decimal)(written - target)) < 1.50m; // a "close enough" band
        Assert.True(naiveToleranceCheck); // the naive check would wrongly call this balanced

        Assert.False(ZeroBalanceCheck.IsBalanced([1_536m], target)); // the real one does not
    }
}
