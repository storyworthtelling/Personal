using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class ShelfMonogramTests
{
    [Theory]
    [InlineData("Finance", "F")]              // one word → one initial
    [InlineData("media", "M")]                // lower-cased → upper
    [InlineData("Smart Home", "SH")]          // two words → two initials
    [InlineData("Reading & Reference", "RR")] // punctuation between words is skipped
    [InlineData("Health & Fitness & More", "HF")] // capped at the first two words
    [InlineData("  Travel  ", "T")]           // surrounding whitespace trimmed
    [InlineData("123 Go", "1G")]              // a digit is a valid monogram char
    public void For_DrawsAOneOrTwoLetterMonogram(string name, string expected)
    {
        Assert.Equal(expected, ShelfMonogram.For(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("& —")] // no letters or digits at all
    public void For_FallsBackToAQuestionMark_WhenNothingToDrawFrom(string name)
    {
        Assert.Equal("?", ShelfMonogram.For(name));
    }
}
