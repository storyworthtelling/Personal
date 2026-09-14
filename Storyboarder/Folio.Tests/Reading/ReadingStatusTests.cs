using Folio.Domain.Reading;

namespace Folio.Tests.Reading;

/// <summary>
/// The sentence the phone shows while it reads.
///
/// <para>The bug this reintroduces is a spinner — a single unchanging string for a quarter of an
/// hour, which DESIGN.md §5 forbids and which is, more practically, indistinguishable from a hang.
/// A read is fifteen minutes behind a notification; if it does not say which voice is reading, the
/// only honest reading of the screen is that something is stuck.</para>
/// </summary>
public class ReadingStatusTests
{
    private static ReadingPass Pass(string id) =>
        ReadingPlan.Passes.Single(pass => pass.Id == id);

    [Fact]
    public void ASectionPassSaysWhatItIsReadingFor()
    {
        var line = ReadingStatus.Sentence(Pass("pacing"), done: 4, total: 17);

        Assert.Equal("Reading for pacing — 5 of 17.", line);
    }

    [Fact]
    public void AReaderPassSaysWhoIsReading()
    {
        var line = ReadingStatus.Sentence(Pass("agent"), done: 10, total: 17);

        Assert.Equal("Reading as the agent — 11 of 17.", line);
    }

    [Fact]
    public void ASynthesisPassSaysWhatItIsDoing()
    {
        var line = ReadingStatus.Sentence(Pass("verdict"), done: 16, total: 17);

        Assert.Equal("Writing the verdict — 17 of 17.", line);
    }

    /// <summary>Seventeen passes, seventeen different sentences — the thing a spinner cannot do.</summary>
    [Fact]
    public void EveryPassGetsItsOwnSentence()
    {
        var lines = ReadingPlan.Passes
            .Select((pass, index) => ReadingStatus.Sentence(pass, index, ReadingPlan.Passes.Count))
            .ToList();

        Assert.Equal(17, lines.Distinct(StringComparer.Ordinal).Count());
        Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line)));
    }

    [Fact]
    public void AFinishedReadSaysSo()
    {
        Assert.Equal("Read.", ReadingStatus.Sentence(null, done: 17, total: 17));
    }

    [Fact]
    public void ARunWithNothingToReadDoesNotCountToZero()
    {
        Assert.Equal("Reading the manuscript…", ReadingStatus.Sentence(null, done: 0, total: 0));
    }

    /// <summary>The count never runs past the total, whatever the caller does with its index.</summary>
    [Theory]
    [InlineData(-3)]
    [InlineData(40)]
    public void TheCountStaysInsideTheRead(int done)
    {
        var line = ReadingStatus.Sentence(Pass("agent"), done, total: 17);

        Assert.DoesNotContain("-", line, StringComparison.Ordinal);
        Assert.DoesNotContain("41 of", line, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTimeLeftIsAboutSomethingRatherThanAPromise()
    {
        var remaining = ReadingStatus.Remaining(done: 2, total: 17);

        Assert.StartsWith("about ", remaining, StringComparison.Ordinal);
        Assert.Contains("minutes left", remaining, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLastPassDoesNotClaimMinutes()
    {
        Assert.Equal("less than a minute left", ReadingStatus.Remaining(done: 17, total: 17));
    }
}
