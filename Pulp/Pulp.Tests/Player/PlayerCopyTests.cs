using Pulp.Domain.Player;

namespace Pulp.Tests.Player;

/// <summary>D315: one player screen, two kinds of video, each with its own words and no other difference.</summary>
public class PlayerCopyTests
{
    [Fact]
    public void ATrailerSaysTrailer()
    {
        Assert.Equal("Trailer", PlayerCopy.FallbackHeader(PlayerKind.Trailer));
        Assert.Equal("Tap play to start the trailer.", PlayerCopy.Status(PlayerKind.Trailer));
    }

    [Fact]
    public void HighlightsSaysHighlights()
    {
        Assert.Equal("Highlights", PlayerCopy.FallbackHeader(PlayerKind.Highlights));
        Assert.Equal("Tap play to start the highlights.", PlayerCopy.Status(PlayerKind.Highlights));
    }

    [Fact]
    public void AKindReadsBackFromItsStoredNameAndAnUnknownNameIsATrailer()
    {
        Assert.Equal(PlayerKind.Highlights, PlayerCopy.Parse(nameof(PlayerKind.Highlights)));
        Assert.Equal(PlayerKind.Trailer, PlayerCopy.Parse(null));
        Assert.Equal(PlayerKind.Trailer, PlayerCopy.Parse("nonsense"));
    }
}
