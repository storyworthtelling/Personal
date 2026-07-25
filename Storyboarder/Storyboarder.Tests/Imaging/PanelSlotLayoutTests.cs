using Storyboarder.Domain.Imaging;
using Storyboarder.Infrastructure.Imaging;
using Xunit;

namespace Storyboarder.Tests.Imaging;

// Verifies that a panel cell is split into non-overlapping regions: the art, and dialogue/caption bands
// that only appear when there's text — so typeset text never sits on top of the artwork.
public class PanelSlotLayoutTests
{
    private static readonly PanelRect Cell = new(100, 200, 800, 1000);

    private static bool Overlaps(PanelRect a, PanelRect b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width &&
        a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    [Fact]
    public void NoText_ArtFillsWholeCell_NoBands()
    {
        var slot = PanelSlotLayout.Compute(Cell, null, null);

        Assert.Null(slot.Dialogue);
        Assert.Null(slot.Caption);
        Assert.Equal(Cell.X, slot.Art.X);
        Assert.Equal(Cell.Y, slot.Art.Y);
        Assert.Equal(Cell.Width, slot.Art.Width);
        Assert.Equal(Cell.Height, slot.Art.Height);
    }

    [Fact]
    public void DialogueOnly_ReservesTopBand_ArtBelow_NoOverlap()
    {
        var slot = PanelSlotLayout.Compute(Cell, "Some dialogue here.", null);

        Assert.NotNull(slot.Dialogue);
        Assert.Null(slot.Caption);
        Assert.Equal(Cell.Y, slot.Dialogue!.Y);              // band hugs the cell top
        Assert.True(slot.Art.Y > slot.Dialogue.Y);           // art starts below it
        Assert.False(Overlaps(slot.Art, slot.Dialogue));
    }

    [Fact]
    public void CaptionOnly_ReservesBottomBand_ArtAbove_NoOverlap()
    {
        var slot = PanelSlotLayout.Compute(Cell, null, "A caption.");

        Assert.NotNull(slot.Caption);
        Assert.Null(slot.Dialogue);
        Assert.Equal(Cell.Y + Cell.Height, slot.Caption!.Y + slot.Caption.Height, 3); // band hugs the cell bottom
        Assert.True(slot.Art.Y + slot.Art.Height < slot.Caption.Y);                    // art ends above it
        Assert.False(Overlaps(slot.Art, slot.Caption));
    }

    [Fact]
    public void BothBands_ArtSitsBetween_AndTouchesNeither()
    {
        var slot = PanelSlotLayout.Compute(Cell, "Top line of dialogue.", "Bottom caption line.");

        Assert.NotNull(slot.Dialogue);
        Assert.NotNull(slot.Caption);
        Assert.False(Overlaps(slot.Art, slot.Dialogue!));
        Assert.False(Overlaps(slot.Art, slot.Caption!));
        Assert.True(slot.Art.Y > slot.Dialogue!.Y);
        Assert.True(slot.Art.Y + slot.Art.Height < slot.Caption!.Y);
    }

    [Fact]
    public void VeryLongText_KeepsArtFromBeingCrushed()
    {
        var longText = string.Join(" ", Enumerable.Repeat("word", 400));

        var slot = PanelSlotLayout.Compute(Cell, longText, longText);

        // However long the text, the bands are clamped so the art keeps a substantial share of the cell.
        Assert.True(slot.Art.Height > Cell.Height * 0.3,
            $"art height {slot.Art.Height} was crushed below the floor");
        Assert.False(Overlaps(slot.Art, slot.Dialogue!));
        Assert.False(Overlaps(slot.Art, slot.Caption!));
    }
}
