using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class GlyphPathTests
{
    [Theory]
    [InlineData("M10 10 L90 10 90 90 10 90 Z")]
    [InlineData("M20,20 C40,0 60,0 80,20 L80,80 Z")]
    [InlineData("m5 5 h90 v90 h-90 z")]
    public void IsValid_AcceptsAMonochromePath(string path)
    {
        Assert.True(GlyphPath.IsValid(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a shopping cart glyph")]              // prose
    [InlineData("<svg><path d=\"M0 0\"/></svg>")]      // full markup, not a bare path
    [InlineData("M10 10")]                              // a bare move draws nothing
    [InlineData("L10 10 20 20")]                        // doesn't start with a move
    public void IsValid_RejectsNonPaths(string? path)
    {
        Assert.False(GlyphPath.IsValid(path));
    }

    [Fact]
    public void IsValid_RejectsAbsurdlyLongInput()
    {
        var huge = "M0 0" + string.Concat(Enumerable.Repeat(" L1 1", 2000));
        Assert.False(GlyphPath.IsValid(huge));
    }

    [Fact]
    public void Extract_PullsThePathFromACodeFence()
    {
        var reply = "```\nM10 10 L90 10 90 90 Z\n```";
        Assert.Equal("M10 10 L90 10 90 90 Z", GlyphPath.Extract(reply));
    }

    [Fact]
    public void Extract_PullsThePathOutOfProse()
    {
        var reply = "Here's a simple house glyph: M10 50 L50 10 90 50 L90 90 10 90 Z — hope that works!";
        var path = GlyphPath.Extract(reply);
        Assert.NotNull(path);
        Assert.True(GlyphPath.IsValid(path));
        Assert.StartsWith("M10 50", path);
    }

    [Fact]
    public void Extract_ReturnsNull_WhenNothingValidIsPresent()
    {
        Assert.Null(GlyphPath.Extract("I couldn't generate a glyph for that."));
    }
}
