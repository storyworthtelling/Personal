using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// Audit 11 §23: a refused camera permission read "The camera is not ready yet." — which says nothing about what to do,
/// and the permission was only asked for once, at launch. The shutter now asks again, and says which case it is in.
/// </summary>
public class CameraWordsTests
{
    [Fact]
    public void AllowedButStartingSaysItIsStarting() =>
        Assert.Equal("The camera is still starting. Try again in a moment.", CameraWords.ForShutter(CameraAccess.Allowed));

    [Fact]
    public void NotYetAllowedAsksAndSaysWhy()
    {
        Assert.True(CameraWords.ShouldAsk(CameraAccess.NotAllowed));
        Assert.Equal("Plate needs the camera to take a picture. Allow it when the phone asks.", CameraWords.ForShutter(CameraAccess.NotAllowed));
    }

    [Fact]
    public void RefusedForGoodSaysWhereToTurnItOn()
    {
        Assert.False(CameraWords.ShouldAsk(CameraAccess.Refused));
        Assert.Equal("Camera access is off for Plate. Turn it on in the phone's settings, under Apps, Plate, Permissions.",
            CameraWords.ForShutter(CameraAccess.Refused));
    }

    [Theory]
    [InlineData(true, false, CameraAccess.Allowed)]
    [InlineData(false, false, CameraAccess.NotAllowed)]
    [InlineData(false, true, CameraAccess.Refused)]
    public void AccessIsReadFromThePermissionAndTheLastAnswer(bool granted, bool lastAnswerWasNoWithoutAsking, CameraAccess expected) =>
        Assert.Equal(expected, CameraWords.Access(granted, lastAnswerWasNoWithoutAsking));
}
