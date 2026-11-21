using Watch.Domain.Kit;

namespace Watch.Tests.Kit;

public class WatchTextTests
{
    // A monospace measure: every character is ten units wide.
    private static double Mono(string text) => text.Length * 10;

    [Theory]
    [InlineData("Finding the song.")]
    [InlineData("Can't reach TIDAL.")]
    [InlineData("Ease off")]
    [InlineData("Is Lee coming?")]
    [InlineData("Pickup moves to 3.15 on Friday.")]
    public void OneSentenceIsFine(string text) => Assert.Equal(WatchTextVerdict.Fits, WatchText.Check(text));

    [Theory]
    [InlineData("Sent. Lee will see it.")]
    [InlineData("Is it late? Robin asked.")]
    public void TwoSentencesRefused(string text) =>
        Assert.Equal(WatchTextVerdict.MoreThanOneSentence, WatchText.Check(text));

    [Fact]
    public void NoExclamation() => Assert.Equal(WatchTextVerdict.Exclaims, WatchText.Check("Sent!"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyRefused(string text) => Assert.Equal(WatchTextVerdict.Empty, WatchText.Check(text));

    [Fact]
    public void ShortIsOneLine()
    {
        var layout = WatchText.Lay("Finding the song.", 200, Mono);

        Assert.Equal(WatchTextVerdict.Fits, layout.Verdict);
        Assert.Equal(new[] { "Finding the song." }, layout.Lines);
    }

    [Fact]
    public void WrapsOnceAtAWord()
    {
        var layout = WatchText.Lay("Needs your phone nearby.", 130, Mono);

        Assert.Equal(WatchTextVerdict.Fits, layout.Verdict);
        Assert.Equal(new[] { "Needs your", "phone nearby." }, layout.Lines);
    }

    [Fact]
    public void NeverAThirdLine()
    {
        var layout = WatchText.Lay("Pickup moves to a quarter past three on Friday.", 120, Mono);

        Assert.Equal(WatchTextVerdict.TooLong, layout.Verdict);
        Assert.Empty(layout.Lines);
    }

    [Fact]
    public void NeverShrinks()
    {
        var widths = new List<double>();
        double Measure(string text)
        {
            var width = Mono(text);
            widths.Add(width);
            return width;
        }

        var layout = WatchText.Lay("Unbelievablylongword", 120, Measure);

        Assert.Equal(WatchTextVerdict.TooLong, layout.Verdict);
        Assert.All(widths, width => Assert.Equal(0, width % 10));
    }

    [Fact]
    public void ALineFitsItsWidth()
    {
        var layout = WatchText.Lay("No rush, I'll save you a spot.", 160, Mono);

        Assert.All(layout.Lines, line => Assert.True(Mono(line) <= 160));
    }

    [Fact]
    public void RefusedSentenceHasNoLines()
    {
        var layout = WatchText.Lay("Sent. Lee will see it.", 400, Mono);

        Assert.Equal(WatchTextVerdict.MoreThanOneSentence, layout.Verdict);
        Assert.Empty(layout.Lines);
    }
}
