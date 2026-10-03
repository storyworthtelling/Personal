using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>D49: a category whose limit couldn't be read raises a notice naming it and where the
/// question went; the same "recomputed from live state, nothing to mark seen" shape AccountNotices
/// already follows (Guard G25's rule, unnamed in the guard ledger only because Release B predates
/// Budget).</summary>
public class BudgetNoticesTests
{
    [Fact]
    public void UnreadableLimit_ProducesANoticeNamingTheCategory()
    {
        var notices = BudgetNotices.Build([new UnreadableLimit("Dining out")]);

        var notice = Assert.Single(notices);
        Assert.Contains("Dining out", notice.Headline);
        Assert.Contains("reMarkable", notice.Detail);
        // Mockup 8 draws this fold in Snowball's in-progress teal "because the budget is waiting, not wrong".
        Assert.Equal(NoticeSeverity.Progress, notice.Severity);
    }

    [Fact]
    public void NoUnreadableLimits_ProducesNoNotices()
    {
        Assert.Empty(BudgetNotices.Build([]));
    }

    [Fact]
    public void EachUnreadableCategory_GetsItsOwnNotice()
    {
        var notices = BudgetNotices.Build([new UnreadableLimit("Dining out"), new UnreadableLimit("Fuel")]);
        Assert.Equal(2, notices.Count);
    }
}
