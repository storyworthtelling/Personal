using Codex.Domain.Web;
using Xunit;

namespace Codex.Tests.Web;

public class WebPermissionPlanTests
{
    private const string Video = WebPermissionPlan.VideoCapture;
    private const string Audio = WebPermissionPlan.AudioCapture;

    // A page asking for the camera in Flyleaf's browser gets it once the phone has said yes to the
    // app: the grant is the page's own video capture, and nothing else.
    [Fact]
    public void VideoRequest_WithCameraAlreadyGranted_GrantsVideoCapture()
    {
        var verdict = WebPermissionPlan.Decide([Video], cameraGranted: true, originAllowed: true);

        Assert.Equal([Video], verdict.Grant);
        Assert.False(verdict.NeedsCameraPrompt);
    }

    // Until the app holds CAMERA, the page waits: the owner is asked once, then the request is
    // granted or refused. Nothing is granted on the strength of the page asking.
    [Fact]
    public void VideoRequest_WithoutCamera_AsksForCameraAndGrantsNothingYet()
    {
        var verdict = WebPermissionPlan.Decide([Video], cameraGranted: false, originAllowed: true);

        Assert.Empty(verdict.Grant);
        Assert.True(verdict.NeedsCameraPrompt);
    }

    // A third-party frame on a page must not be able to open the camera just by being embedded:
    // the origin has to be one the browser would itself follow.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void VideoRequest_FromAnOriginTheBrowserWouldNotFollow_IsDenied(bool cameraGranted)
    {
        var verdict = WebPermissionPlan.Decide([Video], cameraGranted, originAllowed: false);

        Assert.Empty(verdict.Grant);
        Assert.False(verdict.NeedsCameraPrompt);
    }

    // The microphone is a separate permission the app does not hold, so a page asking for audio
    // alone is refused rather than granted on the camera's back.
    [Fact]
    public void AudioOnlyRequest_IsDenied()
    {
        var verdict = WebPermissionPlan.Decide([Audio], cameraGranted: true, originAllowed: true);

        Assert.Empty(verdict.Grant);
        Assert.False(verdict.NeedsCameraPrompt);
    }

    [Fact]
    public void VideoAndAudio_GrantsVideoOnly()
    {
        var verdict = WebPermissionPlan.Decide([Video, Audio], cameraGranted: true, originAllowed: true);

        Assert.Equal([Video], verdict.Grant);
    }

    [Fact]
    public void EmptyRequest_IsDenied()
    {
        var verdict = WebPermissionPlan.Decide([], cameraGranted: true, originAllowed: true);

        Assert.Empty(verdict.Grant);
        Assert.False(verdict.NeedsCameraPrompt);
    }
}
