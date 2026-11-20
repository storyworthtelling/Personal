using Watch.Domain.Kit;

namespace Watch.Tests.Kit;

public class UndoWindowTests
{
    [Fact]
    public void SixSecondsAtLeast() =>
        Assert.Equal(TimeSpan.FromSeconds(6), UndoWindow.For(TimeSpan.FromSeconds(2)));

    [Fact]
    public void LongerWhenAndroidAsks() =>
        Assert.Equal(TimeSpan.FromSeconds(30), UndoWindow.For(TimeSpan.FromSeconds(30)));

    [Fact]
    public void SameAsThePhones() =>
        Assert.Equal(Storyworthtelling.Folds.FoldTiming.WindowFor(TimeSpan.FromSeconds(10)), UndoWindow.For(TimeSpan.FromSeconds(10)));

    [Fact]
    public void RingStartsFull() =>
        Assert.Equal(1, UndoWindow.Remaining(TimeSpan.Zero, TimeSpan.FromSeconds(6)), 9);

    [Fact]
    public void RingEmpties() =>
        Assert.Equal(0.5, UndoWindow.Remaining(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6)), 9);

    [Fact]
    public void RingNeverNegative() =>
        Assert.Equal(0, UndoWindow.Remaining(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(6)), 9);

    [Fact]
    public void OpenUntilTheEnd()
    {
        var window = TimeSpan.FromSeconds(6);
        Assert.True(UndoWindow.IsOpen(TimeSpan.FromMilliseconds(5_999), window));
        Assert.False(UndoWindow.IsOpen(window, window));
    }
}
