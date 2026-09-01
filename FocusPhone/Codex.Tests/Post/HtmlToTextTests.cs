using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// A stopgap, and labelled as one. Post has no HTML renderer yet — the hardened WebView is
/// still Flyleaf's alone — and an HTML-only email has no plain-text part at all, so without
/// this it lists, opens, and shows the owner an empty page with nothing logged, because
/// nothing failed.
///
/// This is not an HTML renderer and does not try to be. It makes an HTML-only message
/// READABLE until the shared web view lands.
/// </summary>
public class HtmlToTextTests
{
    [Fact]
    public void TagsAreStripped()
    {
        Assert.Equal("Hello there", HtmlToText.Convert("<p>Hello <b>there</b></p>"));
    }

    // Block elements are where the line breaks are; without this the whole message
    // collapses into one run-on paragraph.
    [Fact]
    public void BlockElementsBecomeLineBreaks()
    {
        var text = HtmlToText.Convert("<p>First</p><p>Second</p>");

        Assert.Contains("First", text, StringComparison.Ordinal);
        Assert.Contains("Second", text, StringComparison.Ordinal);
        Assert.Contains('\n', text);
    }

    [Theory]
    [InlineData("<br>")]
    [InlineData("<br/>")]
    [InlineData("<br />")]
    public void LineBreakTagsBreakLines(string tag)
    {
        Assert.Contains('\n', HtmlToText.Convert($"a{tag}b"));
    }

    // Style and script content is not prose and must never reach the page — marketing mail
    // carries kilobytes of CSS that would otherwise be the whole message body.
    [Theory]
    [InlineData("<style>p{color:red}</style><p>Hello</p>")]
    [InlineData("<script>var x = 1;</script><p>Hello</p>")]
    [InlineData("<head><title>Ignore me</title></head><body><p>Hello</p></body>")]
    public void NonProseElementsAreDropped(string html)
    {
        Assert.Equal("Hello", HtmlToText.Convert(html));
    }

    [Theory]
    [InlineData("&amp;", "&")]
    [InlineData("&lt;", "<")]
    [InlineData("&gt;", ">")]
    [InlineData("&quot;", "\"")]
    [InlineData("&#39;", "'")]
    // Between words, not at the edges: a leading non-breaking space is trimmed like any
    // other leading whitespace, which is what should happen to it.
    [InlineData("a&nbsp;b", "a b")]
    // Ampersand is decoded LAST, or this becomes a stray bracket.
    [InlineData("&amp;lt;", "&lt;")]
    public void CommonEntitiesAreDecoded(string html, string expected)
    {
        Assert.Equal(expected, HtmlToText.Convert(html));
    }

    // Marketing HTML is mostly whitespace between tags; leaving it produces a message that
    // is thirty blank lines and a word.
    [Fact]
    public void RunsOfBlankLinesCollapse()
    {
        var text = HtmlToText.Convert("<p>A</p>\n\n\n   \n<p>B</p>");

        Assert.DoesNotContain("\n\n\n", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("<p></p>")]
    public void NothingInIsNothingOut(string? html)
    {
        Assert.Equal(string.Empty, HtmlToText.Convert(html));
    }

    // An unclosed tag must not swallow the rest of the message — real mail is not
    // well-formed, and the failure mode of a naive stripper is a blank page.
    [Fact]
    public void AnUnclosedTagDoesNotSwallowTheMessage()
    {
        Assert.Equal("Hello", HtmlToText.Convert("<p>Hello</p><div"));
    }
}
