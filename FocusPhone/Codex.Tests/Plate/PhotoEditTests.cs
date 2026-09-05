using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// The editor's arithmetic. Crop, tint and filter are the one part of this phase with no
/// existing analogue anywhere in the suite — nothing to borrow, so nothing already proven —
/// which is exactly why the decisions sit here rather than inside a bitmap call.
/// </summary>
public class PhotoEditTests
{
    [Fact]
    public void AFreshEditChangesNothing()
    {
        // Load-bearing. An unchanged picture must send its ORIGINAL bytes: re-encoding a
        // JPEG the owner did not touch loses quality for nothing, every single time.
        Assert.True(PhotoEdit.None.IsUnchanged);
    }

    [Fact]
    public void TurningFourTimesIsBackWhereItStarted()
    {
        var edit = PhotoEdit.None.Turn().Turn().Turn().Turn();

        Assert.Equal(0, edit.QuarterTurns);
        Assert.True(edit.IsUnchanged);
    }

    [Fact]
    public void TurningOnceIsAChange()
    {
        Assert.False(PhotoEdit.None.Turn().IsUnchanged);
        Assert.Equal(1, PhotoEdit.None.Turn().QuarterTurns);
    }

    [Fact]
    public void TheWholeFrameIsNotACrop()
    {
        // Dragging the handles back out to the edges has to count as "no crop", or the
        // picture gets re-encoded for a gesture that undid itself.
        Assert.True(PhotoEdit.None.CropTo(new CropBox(0, 0, 1, 1)).IsUnchanged);
    }

    [Fact]
    public void ACropIsKeptInsideThePicture()
    {
        // The handles can be dragged past the edge, and a rect outside the bitmap is a crash
        // in the bitmap call rather than a wrong picture.
        var crop = PhotoEdit.None.CropTo(new CropBox(-0.5, -0.2, 1.4, 1.1)).Crop;

        Assert.Equal(0, crop.Left);
        Assert.Equal(0, crop.Top);
        Assert.Equal(1, crop.Right);
        Assert.Equal(1, crop.Bottom);
    }

    [Fact]
    public void ACropTurnedInsideOutIsIgnoredRatherThanApplied()
    {
        // A drag that crosses over itself produces one of these. Applying it would ask for a
        // bitmap of negative width.
        var edit = PhotoEdit.None.CropTo(new CropBox(0.8, 0.8, 0.2, 0.2));

        Assert.True(edit.IsUnchanged);
    }

    [Theory]
    [InlineData(CropShape.Square, 1.0)]
    [InlineData(CropShape.Portrait, 9.0 / 16.0)]
    public void AShapedCropIsCentredAndHasTheShapeItSaysIt(CropShape shape, double aspect)
    {
        // On a 3:2 landscape picture, so both shapes have to take width away rather than
        // height, and the result must sit in the middle rather than the corner.
        var crop = PhotoEdit.Shaped(shape, 3000, 2000);
        var width = (crop.Right - crop.Left) * 3000;
        var height = (crop.Bottom - crop.Top) * 2000;

        Assert.Equal(aspect, width / height, 3);
        Assert.Equal(1 - crop.Right, crop.Left, 6);
        Assert.Equal(1 - crop.Bottom, crop.Top, 6);
    }

    [Fact]
    public void AFreeCropIsTheWholeFrame()
    {
        var crop = PhotoEdit.Shaped(CropShape.Free, 3000, 2000);

        Assert.Equal(new CropBox(0, 0, 1, 1), crop);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(-9)]
    public void TintCannotBeDrivenPastItsEnds(int steps)
    {
        var tint = PhotoEdit.None.TintBy(steps).Tint;

        Assert.InRange(tint, -PhotoEdit.TintSteps, PhotoEdit.TintSteps);
    }

    [Fact]
    public void TintBackToTheMiddleIsNoChange()
    {
        Assert.True(PhotoEdit.None.TintBy(2).TintBy(-2).IsUnchanged);
    }

    [Fact]
    public void NoFilterIsNoChange()
    {
        Assert.True(PhotoEdit.None.With(PhotoFilter.None).IsUnchanged);
        Assert.False(PhotoEdit.None.With(PhotoFilter.BlackAndWhite).IsUnchanged);
    }

    [Fact]
    public void EveryFilterHasAName()
    {
        // The segmented control shows them by name, and a blank cell would be a filter the
        // owner cannot choose on purpose.
        foreach (var filter in Enum.GetValues<PhotoFilter>())
        {
            Assert.False(string.IsNullOrWhiteSpace(PhotoEdit.NameOf(filter)));
        }
    }

    [Fact]
    public void AnEditIsUndoneAllAtOnce()
    {
        // One way back for the whole editor, because three separate undos for crop, tint and
        // filter is three things to explain.
        var edit = PhotoEdit.None.Turn().TintBy(2).With(PhotoFilter.Faded)
            .CropTo(new CropBox(0.1, 0.1, 0.9, 0.9));

        Assert.False(edit.IsUnchanged);
        Assert.True(PhotoEdit.None.IsUnchanged);
    }
}
