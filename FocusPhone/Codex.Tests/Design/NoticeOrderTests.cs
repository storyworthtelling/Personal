using Codex.Domain.Design;
using Xunit;

namespace Codex.Tests.Design;

/// <summary>
/// R-G27 for the native dog-ear: the same ordering rule the MAUI kit's <c>DogEarState</c> uses (the
/// file is a byte-parallel copy, kept honest by <c>NativeParityTests</c> over there). Newest first by
/// timestamp; undated notices in a stable order; setting an unchanged key again moves nothing.
/// </summary>
public class NoticeOrderTests
{
    private static DateTimeOffset Noon(int month, int day) => new(2026, month, day, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DatedNoticesRunNewestFirst()
    {
        var order = new NoticeOrder();
        order.Set("lost", Noon(9, 23), "lost");
        order.Set("stale", Noon(9, 26), "stale");
        order.Set("trial", Noon(9, 14), "trial");

        Assert.Equal(["stale", "lost", "trial"], order.Keys);
    }

    [Fact]
    public void UndatedNoticesKeepTheOrderTheCornerAlwaysHad_AndReSettingMovesNothing()
    {
        var order = new NoticeOrder();
        order.Set("a", null, "A");
        order.Set("b", null, "B");
        order.Set("c", null, "C");
        Assert.Equal(["c", "b", "a"], order.Keys);

        Assert.False(order.Set("a", null, "A"));
        Assert.False(order.Set("b", null, "B"));

        Assert.Equal(["c", "b", "a"], order.Keys);
    }

    [Fact]
    public void TiesAreNotReBrokenByAReSet()
    {
        var order = new NoticeOrder();
        order.Set("x", Noon(9, 20), "X");
        order.Set("y", Noon(9, 20), "Y");
        order.Set("z", Noon(9, 20), "Z");
        var before = order.Keys.ToList();

        order.Set("y", Noon(9, 20), "Y");

        Assert.Equal(before, order.Keys);
    }

    [Fact]
    public void DatedBeforeUndated()
    {
        var order = new NoticeOrder();
        order.Set("undated", null, "U");
        order.Set("dated", Noon(9, 1), "D");

        Assert.Equal(["dated", "undated"], order.Keys);
    }

    [Fact]
    public void AChangedNoticeIsNewAgain()
    {
        var order = new NoticeOrder();
        order.Set("a", null, "A");
        order.Set("b", null, "B");

        Assert.True(order.Set("a", null, "A changed"));

        Assert.Equal(["a", "b"], order.Keys);
    }

    [Fact]
    public void SetReportsWhetherAnythingVisibleChanged()
    {
        var order = new NoticeOrder();

        Assert.True(order.Set("a", null, "A"));
        Assert.False(order.Set("a", null, "A"));
        Assert.True(order.Remove("a"));
        Assert.False(order.Remove("a"));
        Assert.False(order.Clear());
        order.Set("b", null, "B");
        Assert.True(order.Clear());
        Assert.Empty(order.Keys);
    }
}
