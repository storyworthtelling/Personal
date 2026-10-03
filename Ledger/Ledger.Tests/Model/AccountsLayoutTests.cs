using Index.Domain.Design;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Phase 4, "Spacing" and "Materials" for the Accounts family of screens: the numbers the mockups'
/// CSS gives (<c>.ix{margin-top:8px}</c>, <c>.dv-inst{margin-top:22px}</c>, <c>.pi{margin:-6px 4px 4px 22px}</c>,
/// <c>.dv-well{padding:28px 16px 0 24px}</c>, <c>.dv-haze{background:rgba(241,235,221,.86)}</c>), held in
/// one place so a page cannot keep a private copy that drifts.
/// </summary>
public class AccountsLayoutTests
{
    [Fact]
    public void CardsAreEightApart_AndTheHeadingIsEightFromItsFirstCard()
    {
        Assert.Equal(8, AccountsLayout.CardGapDp);
        Assert.Equal(8, AccountsLayout.HeadingToCardDp);
    }

    [Fact]
    public void AGroupStartsTwentyTwoBelowTheOneBeforeIt() => Assert.Equal(22, AccountsLayout.GroupMarginDp);

    [Fact]
    public void TheWellIsTwentyFourFromTheLeftAndSixteenFromTheRight()
    {
        Assert.Equal(24, AccountsLayout.WellPaddingLeftDp);
        Assert.Equal(16, AccountsLayout.WellPaddingRightDp);
    }

    [Fact]
    public void ThePostItOverlapsItsCardBySixAndIsIndentedTwentyTwo()
    {
        Assert.Equal(22, AccountsLayout.PostItMarginLeftDp);
        Assert.Equal(-6, AccountsLayout.PostItMarginTopDp);
        Assert.Equal(4, AccountsLayout.PostItMarginRightDp);
        Assert.Equal(4, AccountsLayout.PostItMarginBottomDp);
    }

    [Theory]
    [InlineData(8, 4)]    // a card after a post-it: the post-it's own 4dp bottom margin counts toward the 8
    [InlineData(22, 18)]  // a bank's heading after a post-it
    [InlineData(2, 0)]    // a margin smaller than the post-it's own adds nothing
    public void BlockMarginsCollapseAfterAPostIt_AsTheMockupsDo(int nextMarginTop, int expectedGap) =>
        Assert.Equal(expectedGap, AccountsLayout.GapAfterPostIt(nextMarginTop));

    [Fact]
    public void ARemoveRowIsAFortyEightDpTarget() => Assert.Equal(48, AccountsLayout.RemoveHitDp);

    [Fact]
    public void AnActionCardSitsTwentyFourFromTheLeftAndClearsTheBinderOnTheRight()
    {
        // .dv-card{left:24px;right:90px}: 90 is the 66dp binder plus the same 24.
        Assert.Equal(24, AccountsLayout.ActionCardLeftDp);
        Assert.Equal(90, AccountsLayout.ActionCardRightDp);
    }

    [Fact]
    public void TheEmptyStateSitsOneHundredEightyDownAndItsSentenceIsTwoSeventyWide()
    {
        Assert.Equal(180, AccountsLayout.EmptyStateTopDp);
        Assert.Equal(270, AccountsLayout.EmptyStateSentenceWidthDp);
    }

    [Fact]
    public void ABoxedFieldIsFortyFourTall() => Assert.Equal(44, AccountsLayout.FieldHeightDp);

    [Fact]
    public void TheHazeIsPaperDeepAtEightySixPercent() => Assert.Equal(0.86m, MaterialTokens.HazeAlpha);
}
