using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

// Error-handling §8: the page tray, gathered over several trips through Google's scanner, lived only in memory.
// Android killing Plate while the scanner was in front lost every page. The tray is written down on every change
// and read back when the screen is rebuilt.
public class ScanTrayCodecTests
{
    private static readonly DateTime First = new(2026, 11, 8, 9, 0, 0);

    [Fact]
    public void ATrayRoundTripsWithItsDoorItsOrderAndItsTimes()
    {
        var tray = ScanTray.For(ScanDestination.Remarkable)
            .Add(["file:///cache/p1.jpg", "file:///cache/p2.jpg"], First)
            .Add(["file:///cache/p3.jpg"], First.AddMinutes(4))
            .Move(2, 0);

        var read = ScanTrayCodec.Read(ScanTrayCodec.Write(tray))!;

        Assert.Equal(ScanDestination.Remarkable, read.Destination);
        Assert.Equal(["file:///cache/p3.jpg", "file:///cache/p1.jpg", "file:///cache/p2.jpg"], read.Pages.Select(p => p.Reference));
        Assert.Equal(First.AddMinutes(4), read.Pages[0].ScannedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    public void NothingOrSomethingDamagedIsNoTray(string? text)
    {
        Assert.Null(ScanTrayCodec.Read(text));
    }

    [Fact]
    public void AnEmptyTrayIsNotWorthKeeping()
    {
        Assert.Null(ScanTrayCodec.Read(ScanTrayCodec.Write(ScanTray.For(ScanDestination.Drive))));
    }
}
