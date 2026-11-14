using Codex.Domain.Design;
using Xunit;

namespace Codex.Tests.Design;

/// <summary>
/// §7's pinned bar, decided once (audit W8.3b, report 14 §4): the primary goes last, on the right, and
/// takes the width that is left; everything else keeps the order it was given and is sized to its words.
/// A bar with no primary shares its width evenly, as every bar did before.
/// </summary>
public class BarOrderTests
{
    [Fact]
    public void ThePrimaryMovesToTheEndAndIsTheWidest()
    {
        var plan = BarOrder.Arrange([true, false, false, false]);

        Assert.Equal([1, 2, 3, 0], plan.Order);
        Assert.Equal(0, plan.Widest);
    }

    [Fact]
    public void ThePrimaryAlreadyLastStaysThere()
    {
        var plan = BarOrder.Arrange([false, true]);

        Assert.Equal([0, 1], plan.Order);
        Assert.Equal(1, plan.Widest);
    }

    [Fact]
    public void WithNoPrimaryTheOrderIsKeptAndTheWidthIsShared()
    {
        var plan = BarOrder.Arrange([false, false, false]);

        Assert.Equal([0, 1, 2], plan.Order);
        Assert.Null(plan.Widest);
    }

    /// <summary>§7 allows one primary; a bar that offers two keeps both on the right, the last given widest.</summary>
    [Fact]
    public void TwoPrimariesBothGoRightInTheirOrder()
    {
        var plan = BarOrder.Arrange([true, false, true]);

        Assert.Equal([1, 0, 2], plan.Order);
        Assert.Equal(2, plan.Widest);
    }
}
