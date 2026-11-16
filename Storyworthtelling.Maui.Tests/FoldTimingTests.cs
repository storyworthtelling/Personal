using Storyworthtelling.Folds;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// The undo window honours Android's "Time to take action" (audit 10 §5, WCAG 2.2.1): the platform says how long
/// a person needs, and the window is that long when it is longer than the six seconds the design gives everyone.
/// </summary>
public class FoldTimingTests
{
    [Fact]
    public void ARecommendationShorterThanTheDesignKeepsTheDesignsSixSeconds() =>
        Assert.Equal(TimeSpan.FromSeconds(6), FoldTiming.WindowFor(TimeSpan.FromSeconds(2)));

    [Fact]
    public void ALongerRecommendationIsTheWindow() =>
        Assert.Equal(TimeSpan.FromMinutes(1), FoldTiming.WindowFor(TimeSpan.FromMinutes(1)));

    [Fact]
    public void NoRecommendationIsTheDesignsWindow() =>
        Assert.Equal(TimeSpan.FromMilliseconds(FoldTiming.UndoWindowMs), FoldTiming.WindowFor(TimeSpan.Zero));
}
