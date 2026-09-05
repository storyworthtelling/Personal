using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// What another app is asking for. This is the §2.4 component table as a function, because
/// the table is the thing that goes wrong: Android silently omits an app missing a
/// component, with no warning, so the mapping has to be written down somewhere a test can
/// read it rather than only in six attributes on an Activity.
/// </summary>
public class ErrandsTests
{
    [Theory]
    [InlineData("android.media.action.IMAGE_CAPTURE")]
    [InlineData("android.media.action.IMAGE_CAPTURE_SECURE")]
    public void AskingForAPictureIsAPhotoErrand(string action)
    {
        Assert.Equal(Errand.Photo, Errands.For(action, null));
    }

    [Fact]
    public void AskingForAVideoIsAVideoErrand()
    {
        Assert.Equal(Errand.Video, Errands.For("android.media.action.VIDEO_CAPTURE", null));
    }

    [Theory]
    [InlineData("android.media.action.STILL_IMAGE_CAMERA")]
    [InlineData("android.media.action.STILL_IMAGE_CAMERA_SECURE")]
    public void ThePowerButtonJustOpensTheCamera(string action)
    {
        // Nobody is waiting on a result here — the phone was asked to show a camera, which
        // is the app's ordinary self, not an errand with something to return.
        Assert.Equal(Errand.OpenCamera, Errands.For(action, null));
    }

    [Fact]
    public void ViewingAnImageIsShowingIt()
    {
        // Post's attachment route. Without this row, Attachments.cs setting the package
        // produces an activity-not-found rather than a picture.
        Assert.Equal(Errand.ShowImage, Errands.For("android.intent.action.VIEW", "image/jpeg"));
    }

    [Fact]
    public void ViewingSomethingThatIsNotAnImageIsNotOurs()
    {
        // Plate shows pictures. A PDF handed to it by mistake should fall through to
        // whatever actually opens PDFs rather than being displayed as a broken image.
        Assert.Equal(Errand.None, Errands.For("android.intent.action.VIEW", "application/pdf"));
        Assert.Equal(Errand.None, Errands.For("android.intent.action.VIEW", null));
    }

    [Fact]
    public void TheTwoReadsAreTheirOwnErrands()
    {
        Assert.Equal(Errand.ReadCard, Errands.For(ReadRequest.ReadCard, null));
        Assert.Equal(Errand.ReadStatement, Errands.For(ReadRequest.ReadStatement, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("android.intent.action.MAIN")]
    [InlineData("com.example.SOMETHING_ELSE")]
    public void AnythingUnrecognisedIsNoErrandAtAll(string? action)
    {
        // Launching Plate normally arrives here too, and must not be mistaken for a job
        // somebody is blocked on.
        Assert.Equal(Errand.None, Errands.For(action, null));
    }

    [Theory]
    [InlineData(Errand.Photo)]
    [InlineData(Errand.Video)]
    [InlineData(Errand.ReadCard)]
    [InlineData(Errand.ReadStatement)]
    [InlineData(Errand.ShowImage)]
    public void AnErrandBlocksItsCaller(Errand errand)
    {
        // The caller is sitting in onActivityResult. That is why an errand screen drops the
        // binder gutter entirely — there is nowhere else to go, and the frame says so
        // rather than a rule politely asking the owner not to wander.
        Assert.True(Errands.HasAWaitingCaller(errand));
    }

    [Theory]
    [InlineData(Errand.None)]
    [InlineData(Errand.OpenCamera)]
    public void OpeningTheCameraBlocksNobody(Errand errand)
    {
        Assert.False(Errands.HasAWaitingCaller(errand));
    }
}
