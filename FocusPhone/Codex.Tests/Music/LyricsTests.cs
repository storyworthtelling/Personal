using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class LyricsTests
{
    private const string Synced = """
        [ar:Switchfoot]
        [ti:Dare You To Move]
        [00:12.30]Welcome to the planet
        [00:16.10]Welcome to existence
        [01:04.00]Everyone's here
        """;

    [Fact]
    public void Parse_ReadsTimestampedLines()
    {
        var lyrics = LrcParser.Parse(Synced, null);

        Assert.True(lyrics.Timed);
        Assert.Equal(3, lyrics.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(12.3), lyrics.Lines[0].At);
        Assert.Equal("Welcome to the planet", lyrics.Lines[0].Text);
    }

    // [ar:] [ti:] [al:] [length:] and friends are file metadata, not words anyone
    // sings.
    [Fact]
    public void Parse_LeavesOutTheFileMetadata()
    {
        Assert.DoesNotContain(LrcParser.Parse(Synced, null).Lines, line => line.Text.Contains("Switchfoot"));
    }

    // LRC lets one line carry several timestamps, which is how a repeated chorus is
    // written without repeating the words.
    [Fact]
    public void Parse_RepeatsALineWithSeveralTimestamps()
    {
        var lyrics = LrcParser.Parse("[00:10.00][02:30.00]The chorus", null);

        Assert.Equal(2, lyrics.Lines.Count);
        Assert.All(lyrics.Lines, line => Assert.Equal("The chorus", line.Text));
        Assert.Equal(TimeSpan.FromSeconds(150), lyrics.Lines[1].At);
    }

    [Fact]
    public void Parse_PutsLinesInTimeOrder()
    {
        var lyrics = LrcParser.Parse("[01:00.00]Second\n[00:30.00]First", null);

        Assert.Equal(["First", "Second"], lyrics.Lines.Select(line => line.Text));
    }

    // An empty timed line is a rest — an instrumental break — and keeping it is what
    // lets the highlight clear instead of holding the last line through a solo.
    [Fact]
    public void Parse_KeepsATimedSilence()
    {
        var lyrics = LrcParser.Parse("[00:10.00]A word\n[00:20.00]", null);

        Assert.Equal(2, lyrics.Lines.Count);
        Assert.Equal(string.Empty, lyrics.Lines[1].Text);
    }

    // Most lyrics are not timed. They are still worth reading, so they come through
    // as lines with no times and the panel simply does not follow along.
    [Fact]
    public void Parse_FallsBackToPlainWords()
    {
        var lyrics = LrcParser.Parse(null, "Welcome to the planet\nWelcome to existence");

        Assert.False(lyrics.Timed);
        Assert.Equal(2, lyrics.Lines.Count);
        Assert.Equal(TimeSpan.Zero, lyrics.Lines[0].At);
    }

    [Fact]
    public void Parse_PrefersTimedOverPlain()
    {
        Assert.True(LrcParser.Parse(Synced, "plain words").Timed);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", null)]
    public void Parse_WithNothingIsEmpty(string? synced, string? plain)
    {
        Assert.True(LrcParser.Parse(synced, plain).IsEmpty);
    }

    // A file that is all metadata has no words in it, whatever its length suggests.
    [Fact]
    public void Parse_OfMetadataAloneIsEmpty()
    {
        Assert.True(LrcParser.Parse("[ar:Someone]\n[length:03:20]", null).IsEmpty);
    }

    [Fact]
    public void LineAt_IsNothingBeforeTheFirstWord()
    {
        Assert.Equal(-1, LrcParser.Parse(Synced, null).LineAt(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void LineAt_HoldsALineUntilTheNextOne()
    {
        var lyrics = LrcParser.Parse(Synced, null);

        Assert.Equal(0, lyrics.LineAt(TimeSpan.FromSeconds(12.3)));
        Assert.Equal(0, lyrics.LineAt(TimeSpan.FromSeconds(15)));
        Assert.Equal(1, lyrics.LineAt(TimeSpan.FromSeconds(16.1)));
        Assert.Equal(2, lyrics.LineAt(TimeSpan.FromMinutes(5)));
    }

    // Untimed lyrics have no current line; asking for one would highlight the first
    // word of the song for its whole length.
    [Fact]
    public void LineAt_IsNothingForUntimedWords()
    {
        Assert.Equal(-1, LrcParser.Parse(null, "some words").LineAt(TimeSpan.FromMinutes(1)));
    }
}
