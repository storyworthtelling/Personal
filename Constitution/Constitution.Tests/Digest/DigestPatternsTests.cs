using System.Text.RegularExpressions;
using Constitution.Domain.Body;
using Constitution.Domain.Documents;

namespace Constitution.Tests.DigestPages;

/// <summary>C-2.6.2 — the patterns block: headed "not causes", and the only place a correlation appears.</summary>
public class DigestPatternsTests
{
    private static DigestSeries Made(BodySignal signal, double[] weeks) =>
        new(signal, weeks.Select(value => (double?)value).ToList(), weeks[^1], "x", null);

    [Fact]
    public void PearsonKnowsALine()
    {
        Assert.Equal(1.0, Correlation.Pearson([1, 2, 3, 4], [2, 4, 6, 8]), 6);
        Assert.Equal(-1.0, Correlation.Pearson([1, 2, 3, 4], [8, 6, 4, 2]), 6);
        Assert.Equal(0.0, Correlation.Pearson([1, 1, 1, 1], [3, 5, 4, 9]), 6);
    }

    [Fact]
    public void FindsAPairThatMovesTogether()
    {
        var active = Made(BodySignal.ActiveMinutes, [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120]);
        var sleep = Made(BodySignal.Sleep, [6.0, 6.1, 6.3, 6.4, 6.5, 6.7, 6.8, 6.9, 7.0, 7.2, 7.3, 7.4]);
        var found = DigestPatterns.Find([active, sleep]);
        var single = Assert.Single(found);
        Assert.Equal("Weeks with more active minutes also had more sleep.", single.Sentence);
    }

    [Fact]
    public void ANegativePairSaysLess()
    {
        var steps = Made(BodySignal.Steps, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
        var heart = Made(BodySignal.RestingHeartRate, [70, 69, 68, 67, 66, 65, 64, 63, 62, 61, 60, 59]);
        Assert.Equal("Weeks with more steps had a lower resting heart rate.", Assert.Single(DigestPatterns.Find([steps, heart])).Sentence);
    }

    [Fact]
    public void NoiseIsNotAPattern()
    {
        var a = Made(BodySignal.Steps, [5, 1, 4, 2, 5, 3, 1, 4, 2, 5, 1, 3]);
        var b = Made(BodySignal.Sleep, [3, 3, 1, 5, 2, 2, 5, 1, 4, 3, 5, 1]);
        Assert.Empty(DigestPatterns.Find([a, b]));
    }

    [Fact]
    public void TooFewSharedWeeksIsNotEnough()
    {
        var a = new DigestSeries(BodySignal.Steps, [1, 2, 3, 4, 5, null, null, null, null, null, null, null], 5, "x", null);
        var b = new DigestSeries(BodySignal.Sleep, [2, 4, 6, 8, 10, null, null, null, null, null, null, null], 10, "x", null);
        Assert.Empty(DigestPatterns.Find([a, b]));
    }

    [Fact]
    public void NoSentenceClaimsACause()
    {
        var active = Made(BodySignal.ActiveMinutes, [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120]);
        var sleep = Made(BodySignal.Sleep, [6.0, 6.1, 6.3, 6.4, 6.5, 6.7, 6.8, 6.9, 7.0, 7.2, 7.3, 7.4]);
        foreach (var pattern in DigestPatterns.Find([active, sleep]))
        {
            Assert.DoesNotMatch(new Regex("because|cause|leads to|makes|improves|should", RegexOptions.IgnoreCase), pattern.Sentence);
        }
    }

    [Fact]
    public void TheHeadingSaysNotCauses()
    {
        Assert.Equal("Patterns, not causes", DigestPatterns.Heading);
        var series = DigestBody.Series(DigestFixture.Flat(BodySignal.Sleep, 7.0), DigestFixture.Saturday);
        var pages = DigestBody.Pages(series, [], DigestFixture.Saturday, 2, 5);
        Assert.Contains(pages[1].Printed, item => item.Text == DigestPatterns.Heading);
    }
}
