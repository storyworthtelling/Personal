using Codex.Domain.Design;
using Xunit;

namespace Codex.Tests.Design;

public class SampleSizeTests
{
    [Fact]
    public void For_DoesNotShrinkArtworkThatIsAlreadySmallEnough()
    {
        Assert.Equal(1, SampleSize.For(80, 80, 120));
        Assert.Equal(1, SampleSize.For(120, 120, 120));
    }

    [Theory]
    [InlineData(240, 240, 120, 2)]
    [InlineData(480, 480, 120, 4)]
    [InlineData(1000, 1000, 120, 8)]
    public void For_HalvesUntilTheImageFits(int width, int height, int target, int expected)
    {
        Assert.Equal(expected, SampleSize.For(width, height, target));
    }

    // Halving is per-axis in the decoder, so the smaller side decides — sampling by
    // the larger one would decode a wide cover down past the target on its short side.
    [Fact]
    public void For_IsGovernedByTheShorterSide()
    {
        Assert.Equal(2, SampleSize.For(1000, 240, 120));
    }

    [Fact]
    public void For_IsAlwaysAPowerOfTwo()
    {
        for (var width = 1; width <= 2048; width += 37)
        {
            var sample = SampleSize.For(width, width, 100);
            Assert.True((sample & (sample - 1)) == 0, $"{width}px gave sample size {sample}");
        }
    }

    // A cover that failed to decode reports zero dimensions; asking for a sample size
    // then must not divide by zero or loop.
    [Fact]
    public void For_SurvivesAnUnreadableImage()
    {
        Assert.Equal(1, SampleSize.For(0, 0, 120));
        Assert.Equal(1, SampleSize.For(-4, 10, 120));
    }

    [Fact]
    public void For_SurvivesAMeaninglessTarget()
    {
        Assert.Equal(1, SampleSize.For(500, 500, 0));
    }
}
