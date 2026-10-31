using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;

namespace Constitution.Tests.ReadBack;

/// <summary>When a page is trashed and when a document is let go (R4, and the rule that a failure never loses a page).</summary>
public class ReadBackPolicyTests
{
    private static readonly DateOnly Day = new(2026, 11, 3);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(30, true)]
    public void TheDigestGoesAfterThreeDays(int daysLater, bool due)
    {
        Assert.Equal(due, ReadBackPolicy.DigestIsDue(Day, Day.AddDays(daysLater)));
    }

    [Fact]
    public void ADigestIsNeverDueBeforeItsOwnDay()
    {
        Assert.False(ReadBackPolicy.DigestIsDue(Day, Day.AddDays(-1)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(40, true)]
    [InlineData(-2, false)]
    public void ADocumentIsLetGoAfterAWeek(int daysLater, bool gaveUp)
    {
        Assert.Equal(gaveUp, ReadBackPolicy.HasGivenUp(Day, Day.AddDays(daysLater)));
    }

    [Fact]
    public void ThreeDaysIsTheDocumentedNumber()
    {
        Assert.Equal(3, ReadBackPolicy.DigestDays);
        Assert.Equal(7, ReadBackPolicy.GiveUpAfterDays);
    }

    private static PageResolutions Page(int number, params InkResolution[] resolutions) => new(number, resolutions);

    [Fact]
    public void PagesWithAnswersCanGo()
    {
        Assert.Null(ReadBackPolicy.WhyKept([
            Page(2, new InkResolution.Ticked("done"), new InkResolution.Circled("feel", "right")),
            Page(3, new InkResolution.Skipped("done"), new InkResolution.Unchanged("feel")),
        ]));
    }

    [Fact]
    public void AnUnreadBoxKeepsTheDocument()
    {
        var why = ReadBackPolicy.WhyKept([Page(2, new InkResolution.Ticked("done")), Page(3, new InkResolution.NotRead("done", "page not read"))]);

        Assert.NotNull(why);
        Assert.Contains("3", why, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuestionDoesNotKeepTheDocument()
    {
        Assert.Null(ReadBackPolicy.WhyKept([Page(2, new InkResolution.NeedsAsking("done", "squat", "Ticked", "Not ticked", "read:squat:Ticked|Not ticked"))]));
    }

    [Fact]
    public void SilenceNotYetAnAnswerKeepsTheDocument()
    {
        Assert.NotNull(ReadBackPolicy.WhyKept([Page(1, new InkResolution.Undecided("dinner-1"))]));
    }

    [Fact]
    public void UndecidedBesideItsOwnQuestionIsNotKept()
    {
        // A Done box waits on the rating's question (the resolver's own rule); the question is saved, so the page can go.
        Assert.Null(ReadBackPolicy.WhyKept([
            Page(2, new InkResolution.Undecided("done"), new InkResolution.NeedsAsking("feel", "squat", "easy", "hard", "read:squat:easy|hard")),
        ]));
    }

    [Fact]
    public void AQuestionElsewhereDoesNotExcuseSilence()
    {
        Assert.NotNull(ReadBackPolicy.WhyKept([
            Page(2, new InkResolution.Undecided("dinner-1")),
            Page(3, new InkResolution.NeedsAsking("feel", "row", "easy", "hard", "read:row:easy|hard")),
        ]));
    }

    [Fact]
    public void NoPagesAtAllCannotGo()
    {
        Assert.NotNull(ReadBackPolicy.WhyKept([]));
    }
}
