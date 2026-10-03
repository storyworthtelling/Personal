using Index.Domain.Design;
using Xunit;

namespace Index.Tests.Design;

/// <summary>
/// Phase 4 "Spacing" (mockups 6, 7, 8, 9, 16): the numbers the mockups' CSS fixes for Snowball, Budget and
/// Savings — cards 8dp apart (<c>.ix{margin-top:8px}</c>), a heading 8dp above its card, a 22dp group margin
/// (<c>.dv-inst{margin-top:22px}</c>), the well padded 24 left and 16 right (<c>.dv-well</c>), and a post-it
/// stuck on with <c>margin:-6px 4px 4px 22px</c>.
/// </summary>
public class PlanSpacingTests
{
    [Fact]
    public void TheMockupsMeasures()
    {
        Assert.Equal(8d, PlanSpacing.CardGapDp);
        Assert.Equal(8d, PlanSpacing.HeadingToCardDp);
        Assert.Equal(22d, PlanSpacing.GroupMarginDp);
        Assert.Equal(24d, PlanSpacing.WellLeftDp);
        Assert.Equal(16d, PlanSpacing.WellRightDp);
    }

    [Fact]
    public void ThePostItOverlapsItsCardBySixAndIsIndentedTwentyTwo()
    {
        // In a stack with no spacing between children the margin is the CSS one, read as left/top/right/bottom.
        Assert.Equal(new PlanInset(22, -6, 4, 4), PlanSpacing.PostItMargin(stackSpacing: 0));
    }

    [Fact]
    public void InAStackThatSpacesItsChildren_ThePostItsMarginAbsorbsTheSpacing()
    {
        // 8dp of stack spacing sits between card and post-it already, so the post-it pulls back 14 to land 6
        // over the card, and its 4dp bottom margin collapses into the 8dp before the next card (CSS margins do).
        Assert.Equal(new PlanInset(22, -14, 4, 0), PlanSpacing.PostItMargin(stackSpacing: 8));
    }

    [Fact]
    public void TheMeasuresAreTheOnesAccountsIsDrawnOn_TheSameMockupsSameCss()
    {
        Assert.Equal(AccountsLayout.CardGapDp, PlanSpacing.CardGapDp);
        Assert.Equal(AccountsLayout.HeadingToCardDp, PlanSpacing.HeadingToCardDp);
        Assert.Equal(AccountsLayout.GroupMarginDp, PlanSpacing.GroupMarginDp);
        Assert.Equal(AccountsLayout.WellPaddingLeftDp, PlanSpacing.WellLeftDp);
        Assert.Equal(AccountsLayout.WellPaddingRightDp, PlanSpacing.WellRightDp);

        var stacked = PlanSpacing.PostItMargin(stackSpacing: 0);
        Assert.Equal(AccountsLayout.PostItMarginLeftDp, stacked.Left);
        Assert.Equal(AccountsLayout.PostItMarginTopDp, stacked.Top);
        Assert.Equal(AccountsLayout.PostItMarginRightDp, stacked.Right);
        Assert.Equal(AccountsLayout.PostItMarginBottomDp, stacked.Bottom);
    }
}
