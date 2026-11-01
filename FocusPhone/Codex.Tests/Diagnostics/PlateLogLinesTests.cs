using Codex.Domain.Diagnostics;
using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Diagnostics;

// Plate's lines for the log loop (plan §4.5): the DCIM insert at send time, where each scan went and how it
// ended, the recognition's closeness and decision (a number, never the picture or the name), and every errand
// another app sent. Plate kept no file log at all (G3).
public class PlateLogLinesTests
{
    [Fact]
    public void ASendSaysWhatReachedTheCameraRollAndWhatWasPutBack()
    {
        Assert.Equal("SEND 3 to DCIM/Camera", PlateLogLines.Sent(3, 0));
        Assert.Equal("SEND 2 to DCIM/Camera · 1 put back in the Darkroom", PlateLogLines.Sent(2, 1));
    }

    [Theory]
    [InlineData(ScanDestination.Drive, true, null, "SCAN Drive: handed over")]
    [InlineData(ScanDestination.Post, false, "not installed", "SCAN Post: not delivered — not installed")]
    public void AScanDeliverySaysWhereItWentAndHowItEnded(ScanDestination destination, bool delivered, string? reason, string line)
    {
        Assert.Equal(line, PlateLogLines.Delivered(destination, delivered, reason));
    }

    [Fact]
    public void ARecognitionLogsTheClosenessAndTheDecisionButNoName()
    {
        var match = new FaceMatch("contact-1", "Juniper Example", 0.41);

        var line = PlateLogLines.Recognised(2, match);

        Assert.Equal("RECOGNISE 2 faces · nearest 0.41 of 0.62 · suggested someone", line);
        Assert.DoesNotContain("Juniper", line);
    }

    [Fact]
    public void ARecognitionThatFoundNobodySaysSo()
    {
        Assert.Equal("RECOGNISE 1 face · nobody within 0.62", PlateLogLines.Recognised(1, FaceMatch.Unknown));
        Assert.Equal("RECOGNISE no faces", PlateLogLines.Recognised(0, FaceMatch.Unknown));
    }

    [Theory]
    [InlineData("android.media.action.IMAGE_CAPTURE", true, "ERRAND android.media.action.IMAGE_CAPTURE: returned a result")]
    [InlineData("android.media.action.IMAGE_CAPTURE", false, "ERRAND android.media.action.IMAGE_CAPTURE: cancelled")]
    public void AnErrandSaysWhetherItReturned(string action, bool returned, string line)
    {
        Assert.Equal(line, PlateLogLines.Errand(action, returned));
    }
}
