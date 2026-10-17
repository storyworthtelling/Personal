using Constitution.Domain.Scrapbook;

namespace Constitution.Tests.Scrapbook;

/// <summary>
/// C-3.2.3: the ruled lines. The index card writes its lines ON a rule (the line sits at the foot
/// of each 22 dp row, under the text), a red rule closes the label row, and the notepad leaf is
/// ruled every 24 dp from its torn top. Positions are the mockup CSS's gradients, in box space.
/// </summary>
public class ScrapRulingTests
{
    [Fact]
    public void TheIndexHeadRuleClosesTheLabelRow()
    {
        // border 1 + padding-top 9 + a 23 dp label row, the rule being its last dp.
        Assert.Equal(32, ScrapRuling.IndexHeadRuleTop());
    }

    [Fact]
    public void IndexBodyRulesFallOnTheMockupsPitchBelowTheHead()
    {
        var tops = ScrapRuling.IndexBodyRuleTops(140, 22);

        Assert.Equal([54d, 76d, 98d, 120d], tops);
    }

    [Fact]
    public void AnIndexBodyRuleNeverRunsIntoTheBottomBorder()
    {
        for (var height = 40; height < 220; height++)
        {
            Assert.All(ScrapRuling.IndexBodyRuleTops(height, 22),
                top => Assert.True(top + ScrapRuling.RuleThicknessDp <= height - 1, $"height {height}, rule at {top}"));
        }
    }

    [Fact]
    public void ACardTooShortForALineHasNoBodyRules()
    {
        Assert.Empty(ScrapRuling.IndexBodyRuleTops(40, 22));
    }

    [Fact]
    public void NotepadRulesStartAtTheFoot23AndRepeatEveryPitch()
    {
        var tops = ScrapRuling.NotepadRuleTops(130, 24);

        Assert.Equal([23d, 47d, 71d, 95d, 119d], tops);
    }

    [Fact]
    public void ANotepadRuleNeverRunsIntoTheBottomBorder()
    {
        for (var height = 20; height < 200; height++)
        {
            Assert.All(ScrapRuling.NotepadRuleTops(height, 24),
                top => Assert.True(top + ScrapRuling.RuleThicknessDp <= height - 1, $"height {height}, rule at {top}"));
        }
    }

    [Fact]
    public void ARuleCannotHavePitchZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScrapRuling.IndexBodyRuleTops(100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScrapRuling.NotepadRuleTops(100, -1));
    }
}
