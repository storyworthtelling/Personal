using Codex.Domain.Audio;
using Xunit;

namespace Codex.Tests.Audio;

public class FeedTextTests
{
    [Fact]
    public void ToPlainText_Null_ReturnsNull()
    {
        Assert.Null(FeedText.ToPlainText(null));
    }

    [Fact]
    public void ToPlainText_Whitespace_ReturnsNull()
    {
        Assert.Null(FeedText.ToPlainText("   \n  "));
    }

    [Fact]
    public void ToPlainText_PlainProse_IsUnchanged()
    {
        Assert.Equal("A crossing that takes four minutes.",
            FeedText.ToPlainText("A crossing that takes four minutes."));
    }

    [Fact]
    public void ToPlainText_StripsTags()
    {
        Assert.Equal("We talk to the boatman.",
            FeedText.ToPlainText("<p>We talk to <em>the boatman</em>.</p>"));
    }

    // Block tags are where sentences end; without this the whole description
    // runs together into one line in the Notes panel.
    [Fact]
    public void ToPlainText_BlockTagsBecomeLineBreaks()
    {
        Assert.Equal("First paragraph.\nSecond paragraph.",
            FeedText.ToPlainText("<p>First paragraph.</p><p>Second paragraph.</p>"));
    }

    [Fact]
    public void ToPlainText_BreakTagBecomesLineBreak()
    {
        Assert.Equal("Line one\nLine two", FeedText.ToPlainText("Line one<br/>Line two"));
    }

    [Fact]
    public void ToPlainText_DecodesEntities()
    {
        Assert.Equal("Kettle & Pine — \"Long Wave\"",
            FeedText.ToPlainText("Kettle &amp; Pine &mdash; &quot;Long Wave&quot;"));
    }

    [Fact]
    public void ToPlainText_DecodesNumericEntities()
    {
        Assert.Equal("café — open", FeedText.ToPlainText("caf&#233; &#x2014; open"));
    }

    [Fact]
    public void ToPlainText_CollapsesRunsOfWhitespace()
    {
        Assert.Equal("One two three", FeedText.ToPlainText("One   two\t\tthree"));
    }

    [Fact]
    public void ToPlainText_CollapsesRunsOfBlankLines()
    {
        Assert.Equal("One\nTwo", FeedText.ToPlainText("<p>One</p><br/><br/><p>Two</p>"));
    }

    // A description that is nothing but markup has no text to show, and an empty
    // Notes panel should read as absent rather than as a blank page.
    [Fact]
    public void ToPlainText_MarkupOnly_ReturnsNull()
    {
        Assert.Null(FeedText.ToPlainText("<p></p><br/>"));
    }

    [Fact]
    public void ToPlainText_UnclosedTag_DoesNotSwallowTheRest()
    {
        Assert.Equal("Recorded on the slip.", FeedText.ToPlainText("Recorded on the slip.<p"));
    }

    [Fact]
    public void ToPlainText_CdataStyleContent_KeepsTheText()
    {
        Assert.Equal("Notes with <angle> brackets described",
            FeedText.ToPlainText("Notes with &lt;angle&gt; brackets described"));
    }
}
