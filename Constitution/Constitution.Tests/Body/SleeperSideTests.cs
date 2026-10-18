using Constitution.Domain;
using Constitution.Infrastructure.SleepNumber;

namespace Constitution.Tests.Body;

/// <summary>Which side of the bed is mine: stored as a word under a frozen key, and never guessed.</summary>
public class SleeperSideTests
{
    [Theory]
    [InlineData("left", BedSide.Left)]
    [InlineData("right", BedSide.Right)]
    [InlineData("LEFT", BedSide.Left)]
    [InlineData(" Right ", BedSide.Right)]
    public void AWordIsASide(string stored, BedSide expected)
    {
        Assert.Equal(expected, Constitution.Infrastructure.Body.SleeperSideCodec.Read(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("middle")]
    [InlineData("0")]
    public void AnythingElseIsNoSideChosen(string? stored)
    {
        Assert.Null(Constitution.Infrastructure.Body.SleeperSideCodec.Read(stored));
    }

    [Fact]
    public void ASideIsWrittenAsItsWord()
    {
        Assert.Equal("left", Constitution.Infrastructure.Body.SleeperSideCodec.Write(BedSide.Left));
        Assert.Equal("right", Constitution.Infrastructure.Body.SleeperSideCodec.Write(BedSide.Right));
        Assert.Equal(string.Empty, Constitution.Infrastructure.Body.SleeperSideCodec.Write(null));
    }

    [Fact]
    public void TheKeyIsFrozenUnderItsLiteral()
    {
        Assert.Equal("sleeper-side", ConstitutionIdentity.SleeperSideKey);
        Assert.Contains("sleeper-side", ConstitutionIdentity.PreferenceKeys);
    }
}
