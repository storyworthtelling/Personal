using Foreword.Domain.Install;
using Xunit;

namespace Foreword.Tests.Install;

/// <summary>
/// Turning a photo off a phone into a mark a page can carry.
///
/// <para>Two things have to be true of it and neither is obvious from the picture. It must be
/// <b>square</b>, because the page draws it in a circle and a rectangle masked to a circle crops
/// whatever happened to be at the edges — usually the top of somebody's head. And it must be
/// <b>small</b>, because it is going into an encrypted vault as base64, and a phone photo is four
/// megabytes before it is encoded.</para>
/// </summary>
public class MedallionImageTests
{
    [Fact]
    public void ASquarePhotoIsScaledAndNotCropped()
    {
        var plan = MedallionImage.Plan(1000, 1000)!.Value;

        Assert.Equal(0, plan.SourceX);
        Assert.Equal(0, plan.SourceY);
        Assert.Equal(1000, plan.SourceEdge);
        Assert.Equal(MedallionImage.MaxEdge, plan.TargetEdge);
    }

    /// <summary>
    /// A landscape photo is cropped from the <b>middle</b>. Cropping from the left is how a group
    /// photo becomes a picture of somebody's shoulder.
    /// </summary>
    [Fact]
    public void ALandscapePhotoIsCroppedFromTheMiddle()
    {
        var plan = MedallionImage.Plan(1600, 900)!.Value;

        Assert.Equal(900, plan.SourceEdge);
        Assert.Equal((1600 - 900) / 2, plan.SourceX);
        Assert.Equal(0, plan.SourceY);
    }

    [Fact]
    public void APortraitPhotoIsCroppedFromTheMiddleToo()
    {
        var plan = MedallionImage.Plan(900, 1600)!.Value;

        Assert.Equal(900, plan.SourceEdge);
        Assert.Equal(0, plan.SourceX);
        Assert.Equal((1600 - 900) / 2, plan.SourceY);
    }

    /// <summary>
    /// An odd difference cannot be split evenly. It leans one way by a single pixel rather than
    /// rounding to a crop that is one pixel wider than the photo.
    /// </summary>
    [Fact]
    public void AnOddDifferenceStaysInsideThePhoto()
    {
        var plan = MedallionImage.Plan(101, 50)!.Value;

        Assert.Equal(50, plan.SourceEdge);
        Assert.True(plan.SourceX + plan.SourceEdge <= 101);
        Assert.True(plan.SourceX >= 0);
    }

    /// <summary>
    /// Never upscaled. A small photo stays its own size — blowing it up adds no detail and makes
    /// the vault entry bigger for nothing.
    /// </summary>
    [Fact]
    public void ASmallPhotoIsNotBlownUp()
    {
        var plan = MedallionImage.Plan(120, 160)!.Value;

        Assert.Equal(120, plan.SourceEdge);
        Assert.Equal(120, plan.TargetEdge);
    }

    [Fact]
    public void APhotoExactlyAtTheCapIsLeftAlone()
    {
        var plan = MedallionImage.Plan(MedallionImage.MaxEdge, MedallionImage.MaxEdge)!.Value;

        Assert.Equal(MedallionImage.MaxEdge, plan.TargetEdge);
    }

    /// <summary>
    /// Nothing to plan is not an error. An unreadable file, a zero-byte image — the answer is no
    /// mark, which is a page the owner has not signed yet rather than a crash in Settings.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 0)]
    [InlineData(0, 100)]
    [InlineData(-4, 100)]
    public void NothingToPlanIsNothing(int width, int height)
    {
        Assert.Null(MedallionImage.Plan(width, height));
    }

    /// <summary>
    /// The cap is big enough for what the page draws. The mark prints at 96 points, and a source
    /// smaller than that would be visibly soft on a 300-dpi sheet.
    /// </summary>
    [Fact]
    public void TheCapIsLargerThanThePageDrawsIt()
    {
        Assert.True(MedallionImage.MaxEdge >= 2 * MedallionImage.PrintedPoints);
    }
}
