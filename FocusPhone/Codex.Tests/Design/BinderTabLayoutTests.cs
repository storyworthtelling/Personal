using Codex.Domain.Design;
using Xunit;

namespace Codex.Tests.Design;

public class BinderTabLayoutTests
{
    // Score's six areas, the frame the mockups settled on.
    private const int Six = 6;

    [Fact]
    public void Tabs_AreLaidOutInOrder()
    {
        var tabs = BinderTabLayout.Compute(Six, wellHeightDp: 680);

        Assert.Equal(Six, tabs.Count);
        for (var i = 1; i < tabs.Count; i++)
        {
            Assert.True(tabs[i].TopDp > tabs[i - 1].TopDp, $"Tab {i} does not follow tab {i - 1}");
        }
    }

    [Fact]
    public void Tabs_DoNotOverlap()
    {
        var tabs = BinderTabLayout.Compute(Six, wellHeightDp: 680);

        for (var i = 1; i < tabs.Count; i++)
        {
            Assert.True(tabs[i].TopDp >= tabs[i - 1].BottomDp, $"Tab {i} overlaps tab {i - 1}");
        }
    }

    // §5 is strict about this: nothing tappable is under 48dp, and a tab you miss
    // is worse than a tab you cannot read.
    [Fact]
    public void EveryTab_ClearsTheTouchTarget()
    {
        foreach (var count in new[] { 2, 4, 5, 6 })
        {
            var tabs = BinderTabLayout.Compute(count, wellHeightDp: 680);
            Assert.All(tabs, tab => Assert.True(tab.HeightDp >= DesignTokens.Shape.TouchTargetDp,
                $"With {count} tabs, a tab is {tab.HeightDp}dp"));
        }
    }

    [Fact]
    public void Tabs_FitInsideTheWell()
    {
        var tabs = BinderTabLayout.Compute(Six, wellHeightDp: 680);

        Assert.True(tabs[0].TopDp >= 0);
        Assert.True(tabs[^1].BottomDp <= 680);
    }

    // The strip is centred rather than top-aligned so the frame stays balanced when
    // an app carries fewer areas than the ceiling.
    [Fact]
    public void Tabs_AreCentredInTheWell()
    {
        var tabs = BinderTabLayout.Compute(4, wellHeightDp: 680);

        var above = tabs[0].TopDp;
        var below = 680 - tabs[^1].BottomDp;
        Assert.InRange(Math.Abs(above - below), 0, 1);
    }

    // Six is the practical ceiling (DESIGN.md §9); past it the strip scrolls, which
    // costs a tab its at-a-glance position.
    [Fact]
    public void BeyondTheCeiling_TheStripScrolls()
    {
        Assert.False(BinderTabLayout.Compute(6, wellHeightDp: 680).Scrolls);
        Assert.True(BinderTabLayout.Compute(9, wellHeightDp: 400).Scrolls);
    }

    [Fact]
    public void AScrollingStrip_StillClearsTheTouchTarget()
    {
        var tabs = BinderTabLayout.Compute(9, wellHeightDp: 400);

        Assert.All(tabs, tab => Assert.True(tab.HeightDp >= DesignTokens.Shape.TouchTargetDp));
    }

    [Fact]
    public void HitTest_FindsTheTabUnderThePoint()
    {
        var tabs = BinderTabLayout.Compute(Six, wellHeightDp: 680);
        var third = tabs[2];

        Assert.Equal(2, tabs.IndexAt(third.TopDp + 1));
        Assert.Equal(2, tabs.IndexAt(third.BottomDp - 1));
    }

    [Fact]
    public void HitTest_OutsideTheStrip_FindsNothing()
    {
        var tabs = BinderTabLayout.Compute(Six, wellHeightDp: 680);

        Assert.Null(tabs.IndexAt(-5));
        Assert.Null(tabs.IndexAt(9999));
    }

    [Fact]
    public void HitTest_InAGapBetweenTabs_FindsNothing()
    {
        var tabs = BinderTabLayout.Compute(Six, wellHeightDp: 680);

        // The gap sits between one tab's bottom and the next tab's top.
        if (tabs[1].TopDp > tabs[0].BottomDp)
        {
            Assert.Null(tabs.IndexAt(tabs[0].BottomDp));
        }
    }

    [Fact]
    public void NoTabs_IsAnEmptyStrip()
    {
        var tabs = BinderTabLayout.Compute(0, wellHeightDp: 680);

        Assert.Empty(tabs);
        Assert.Null(tabs.IndexAt(10));
    }

    [Fact]
    public void ANegativeCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BinderTabLayout.Compute(-1, 680));
    }
}
