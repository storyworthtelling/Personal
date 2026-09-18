using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// The free-crop drag math: which handle a touch grabs, and where it lands. Kept pure and
/// pixel-shaped so it is tested without a device or a real bitmap.
/// </summary>
public class CropGestureTests
{
    // A 3:2 landscape picture with the default whole-frame box, so all four corners sit at
    // its actual corners.
    private const double Width = 3000;
    private const double Height = 2000;

    [Theory]
    [InlineData(0, 0, CropHandle.TopLeft)]
    [InlineData(3000, 0, CropHandle.TopRight)]
    [InlineData(0, 2000, CropHandle.BottomLeft)]
    [InlineData(3000, 2000, CropHandle.BottomRight)]
    public void ATouchOnACornerGrabsThatHandle(double x, double y, CropHandle expected)
    {
        var nearest = CropGesture.Nearest(CropBox.Whole, Width, Height, x, y, radius: 50);

        Assert.Equal(expected, nearest);
    }

    [Fact]
    public void ATouchInTheMiddleGrabsNothing()
    {
        var nearest = CropGesture.Nearest(CropBox.Whole, Width, Height, Width / 2, Height / 2, radius: 50);

        Assert.Null(nearest);
    }

    [Fact]
    public void ATouchJustOutsideTheRadiusGrabsNothing()
    {
        var nearest = CropGesture.Nearest(CropBox.Whole, Width, Height, 60, 0, radius: 50);

        Assert.Null(nearest);
    }

    [Fact]
    public void TheNearestCornerWinsEvenOffARealHandle()
    {
        // A touch a little inside the top-left corner should still find it, and must not be
        // confused with the top-right corner across the whole width of the picture.
        var nearest = CropGesture.Nearest(CropBox.Whole, Width, Height, 10, 10, radius: 50);

        Assert.Equal(CropHandle.TopLeft, nearest);
    }

    [Fact]
    public void ATouchNearAnUnevenCornerStillLandsOnTheRightHandle()
    {
        // The picture is 3:2, not square, so this only passes if a corner's fraction is
        // multiplied by the matching dimension (width for X, height for Y) rather than the
        // other one — a mixed-up multiplication would place TopRight at (2000, 0) instead of
        // (3000, 0) and miss this touch by 940px.
        var nearest = CropGesture.Nearest(CropBox.Whole, Width, Height, 2960, 20, radius: 50);

        Assert.Equal(CropHandle.TopRight, nearest);
    }

    [Theory]
    [InlineData(CropHandle.TopLeft, 300, 200, 0.1, 0.1, 1, 1)]
    [InlineData(CropHandle.TopRight, 2700, 200, 0, 0.1, 0.9, 1)]
    [InlineData(CropHandle.BottomLeft, 300, 1800, 0.1, 0, 1, 0.9)]
    [InlineData(CropHandle.BottomRight, 2700, 1800, 0, 0, 0.9, 0.9)]
    public void MovingAHandleMovesOnlyItsOwnCorner(
        CropHandle handle, double x, double y,
        double left, double top, double right, double bottom)
    {
        var moved = CropGesture.Moved(CropBox.Whole, handle, Width, Height, x, y);

        Assert.Equal(left, moved.Left, 6);
        Assert.Equal(top, moved.Top, 6);
        Assert.Equal(right, moved.Right, 6);
        Assert.Equal(bottom, moved.Bottom, 6);
    }

    [Fact]
    public void MovingAHandlePastTheOppositeSideStillProducesACoordinate()
    {
        // The pure maths does not reject anything — that decision belongs to
        // PhotoEdit.CropTo, the one place a box is judged valid, once a caller routes this
        // result through it.
        var moved = CropGesture.Moved(CropBox.Whole, CropHandle.TopLeft, Width, Height, 3300, 100);

        Assert.Equal(1.1, moved.Left, 6);
    }

    [Fact]
    public void MovingAHandleAndCommittingThroughCropToRejectsAnInversion()
    {
        var draggedPastTheOppositeCorner = CropGesture.Moved(
            new CropBox(0.2, 0.2, 0.8, 0.8), CropHandle.TopLeft, Width, Height, x: 2700, y: 200);

        var edit = PhotoEdit.None.CropTo(new CropBox(0.2, 0.2, 0.8, 0.8))
            .CropTo(draggedPastTheOppositeCorner);

        // Dragging top-left past the fixed right edge would invert the box, so CropTo leaves
        // the prior, still-valid box in place rather than accepting negative width.
        Assert.Equal(new CropBox(0.2, 0.2, 0.8, 0.8), edit.Crop);
    }

    [Fact]
    public void MovingAHandleAndCommittingThroughCropToClampsToThePicture()
    {
        var draggedOffTheEdge = CropGesture.Moved(CropBox.Whole, CropHandle.BottomRight, Width, Height, 3500, 2400);

        var edit = PhotoEdit.None.CropTo(draggedOffTheEdge);

        Assert.Equal(new CropBox(0, 0, 1, 1), edit.Crop);
    }
}
