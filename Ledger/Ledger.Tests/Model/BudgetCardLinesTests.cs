using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Remediation 3.2 item 1 (D142, mockup 8 frame 2): in tracking-only mode each card shows what has been
/// spent so far AND last month's spend beneath it for comparison. The comment on the old code said last
/// month went "in the post-it, not this row"; the mockup draws it as the card's second row.
/// </summary>
public class BudgetCardLinesTests
{
    [Fact]
    public void TrackingCard_ShowsSpentSoFar_ThenLastMonthBeneathIt()
    {
        var lines = BudgetCardLines.ForTracking(spent: 361.45m, lastMonth: 548.30m);

        Assert.Equal(2, lines.Count);
        Assert.Equal(new BudgetCardLine("Spent so far", 361.45m, Secondary: false), lines[0]);
        Assert.Equal(new BudgetCardLine("Last month", 548.30m, Secondary: true), lines[1]);
    }

    [Fact]
    public void ACategoryWithNoRecordedLastMonth_ShowsOnlySpentSoFar_NotAMadeUpZero()
    {
        // The first month a category is tracked has nothing to compare against; "Last month $0.00"
        // would claim it was measured and came to nothing.
        var lines = BudgetCardLines.ForTracking(spent: 96.40m, lastMonth: null);

        var only = Assert.Single(lines);
        Assert.Equal("Spent so far", only.Label);
    }

    [Fact]
    public void ARecordedZero_IsShown_BecauseItWasMeasured()
    {
        var lines = BudgetCardLines.ForTracking(spent: 10m, lastMonth: 0m);

        Assert.Equal("Last month", lines[1].Label);
        Assert.Equal(0m, lines[1].Amount);
    }
}
