using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

public class BudgetCardStateTests
{
    [Fact]
    public void NoLimitAndSomeSpending_IsNoLimit_NotZeroLeft()
    {
        var state = BudgetCardState.Decide(limit: 0m, spent: 361.45m, trackingOnly: false, limitUnreadable: false);

        Assert.Equal(BudgetCardKind.NoLimit, state.Kind);
        Assert.Equal(361.45m, state.Spent);
    }

    [Fact]
    public void NoLimitAndNoSpending_IsStillNoLimit()
    {
        Assert.Equal(BudgetCardKind.NoLimit, BudgetCardState.Decide(0m, 0m, false, false).Kind);
    }

    [Fact]
    public void ARealLimit_KeepsTheRemainingAndTheOverRule()
    {
        var under = BudgetCardState.Decide(600m, 238.55m, false, false);
        Assert.Equal(BudgetCardKind.WithinLimit, under.Kind);
        Assert.Equal(361.45m, under.Remaining);

        var over = BudgetCardState.Decide(150m, 183.20m, false, false);
        Assert.Equal(BudgetCardKind.OverLimit, over.Kind);
        Assert.Equal(-33.20m, over.Remaining);
    }

    [Fact]
    public void TrackingOnlyAndUnreadableOutrankTheLimitArithmetic()
    {
        Assert.Equal(BudgetCardKind.TrackingOnly, BudgetCardState.Decide(0m, 5m, trackingOnly: true, limitUnreadable: false).Kind);
        Assert.Equal(BudgetCardKind.Unreadable, BudgetCardState.Decide(0m, 5m, trackingOnly: false, limitUnreadable: true).Kind);
    }
}
