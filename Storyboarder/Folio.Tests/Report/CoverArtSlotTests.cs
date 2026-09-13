using Folio.Domain.Report;

namespace Folio.Tests.Report;

/// <summary>
/// G15 — the jacket is keyline-framed and never bled.
///
/// <para>The bug this reintroduces is full-bleeding the cover art: running the image to the trim so
/// the report cover becomes the novel's own jacket. Nothing else in the suite would notice. The
/// page would still render, the PDF would still open, and the only thing lost is the distinction
/// DESIGN.md §7 draws between artwork <i>on</i> a document and a document made <i>of</i> artwork.</para>
/// </summary>
public class CoverArtSlotTests
{
    [Fact]
    public void TheSlotSitsInsideTheContentColumnWithPaperOnEverySide()
    {
        var slot = CoverArtSlot.Place(top: 120f);

        Assert.True(slot.Left > PageGeometry.ContentLeft - 0.001f,
            $"The slot's left edge ({slot.Left}) is outside the content column ({PageGeometry.ContentLeft}).");
        Assert.True(slot.Right < PageGeometry.ContentRight + 0.001f,
            $"The slot's right edge ({slot.Right}) is outside the content column ({PageGeometry.ContentRight}).");
        Assert.True(slot.Bottom < PageGeometry.ContentBottom,
            $"The slot's foot ({slot.Bottom}) runs past the content box ({PageGeometry.ContentBottom}).");

        // The margin the eye actually reads: paper between the keyline and the trim, on both sides.
        Assert.True(slot.Left >= PageGeometry.SideMargin,
            "A bled cover: there is no paper between the artwork and the edge of the sheet.");
        Assert.True(PageGeometry.PageWidth - slot.Right >= PageGeometry.SideMargin,
            "A bled cover: there is no paper between the artwork and the edge of the sheet.");
    }

    [Fact]
    public void TheSlotIsCentredInTheColumn()
    {
        var slot = CoverArtSlot.Place(top: 120f);
        var leftGap = slot.Left - PageGeometry.ContentLeft;
        var rightGap = PageGeometry.ContentRight - slot.Right;

        Assert.Equal(leftGap, rightGap, 3);
    }

    [Fact]
    public void ItIsDrawnAtTheSizeTheMockupSpecifies()
    {
        var slot = CoverArtSlot.Place(top: 120f);

        Assert.Equal(318f, slot.Width, 3);
        Assert.Equal(398f, slot.Height, 3);
        Assert.True(CoverArtSlot.KeylineWidth > 0, "The slot has no keyline, so it is not framed at all.");
    }

    /// <summary>
    /// The 2:3 jacket the generator actually asks Gemini for, against the 0.799 slot the page draws:
    /// the two do not agree, so the crop takes the full width and trims equally top and bottom.
    /// </summary>
    [Fact]
    public void ATallJacketIsCentreCroppedRatherThanLetterboxed()
    {
        var crop = CoverArtSlot.Crop(1024, 1536);

        Assert.Equal(0f, crop.Left, 3);
        Assert.Equal(1024f, crop.Right, 3);
        Assert.Equal(crop.Top, 1536f - crop.Bottom, 3);
        Assert.Equal(CoverArtSlot.Width / CoverArtSlot.Height, crop.Width / crop.Height, 3);
    }

    [Fact]
    public void AWideJacketIsTrimmedAtTheSides()
    {
        var crop = CoverArtSlot.Crop(2000, 1000);

        Assert.Equal(0f, crop.Top, 3);
        Assert.Equal(1000f, crop.Bottom, 3);
        Assert.Equal(crop.Left, 2000f - crop.Right, 3);
        Assert.Equal(CoverArtSlot.Width / CoverArtSlot.Height, crop.Width / crop.Height, 3);
    }

    [Fact]
    public void AJacketAlreadyAtTheSlotsAspectIsTakenWhole()
    {
        var crop = CoverArtSlot.Crop(636, 796);

        Assert.Equal(0f, crop.Left, 2);
        Assert.Equal(0f, crop.Top, 2);
        Assert.Equal(636f, crop.Right, 2);
        Assert.Equal(796f, crop.Bottom, 2);
    }

    [Fact]
    public void AnEmptyImageIsRefusedRatherThanCropped()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CoverArtSlot.Crop(0, 0));
    }
}
