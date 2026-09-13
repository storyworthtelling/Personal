namespace Folio.Domain.Report;

/// <summary>A rectangle in page points, or in an image's own pixels. Left/top inclusive.</summary>
public readonly record struct SlotRect(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
}

/// <summary>
/// Where the book's own jacket sits on the report cover, and which part of it is shown.
///
/// <para><b>Never bled.</b> DESIGN.md §7 gives artwork a <i>slot</i>: a framed rectangle sitting
/// inside the content column with paper visible on every side, not an image run to the trim. A
/// bled cover is a book jacket; this is a report <i>about</i> a book, and the distinction is the
/// whole reason the page reads as a document rather than as the novel's own cover. So the slot is
/// placed inside the content column and drawn with a hairline keyline, and
/// <see cref="CoverArtSlotBleeds"/> is what fails if someone widens it back to the sheet.</para>
///
/// <para><b>Why cover-fit rather than letterbox.</b> The slot is 318 × 398 (0.799) and the jacket
/// generator asks Gemini for 2:3 (0.667), so the two do not agree and one of them has to give. The
/// drawing is the specification and it crops (<c>object-fit: cover</c>), which is also the honest
/// answer: letterboxing would put bands of shade inside a keyline frame, which looks like a
/// rendering failure rather than a margin. <see cref="Crop"/> is that decision, pure, so the
/// centre-crop is tested rather than trusted to a canvas call.</para>
/// </summary>
public static class CoverArtSlot
{
    /// <summary>The drawn slot, in points.</summary>
    public const float Width = 318f;
    public const float Height = 398f;

    /// <summary>The keyline around it — one point of hairline, DESIGN.md §7.</summary>
    public const float KeylineWidth = 1f;

    /// <summary>
    /// The slot, centred in the content column with its top edge at <paramref name="top"/>.
    /// </summary>
    public static SlotRect Place(float top)
    {
        var left = PageGeometry.PageWidth / 2f - Width / 2f;
        return new SlotRect(left, top, left + Width, top + Height);
    }

    /// <summary>
    /// The part of the jacket that fills the slot: the largest centred rectangle of the image with
    /// the slot's aspect. An image already at the slot's aspect is taken whole.
    /// </summary>
    public static SlotRect Crop(int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidth),
                $"A jacket of {imageWidth}×{imageHeight} cannot be cropped; the decode should have "
                + "failed before this point rather than handing on an empty image.");
        }

        var slotAspect = Width / Height;
        var imageAspect = (float)imageWidth / imageHeight;

        if (imageAspect > slotAspect)
        {
            // Wider than the slot: keep full height, trim the sides.
            var width = imageHeight * slotAspect;
            var inset = (imageWidth - width) / 2f;
            return new SlotRect(inset, 0, imageWidth - inset, imageHeight);
        }

        // Taller than the slot (the 2:3 jacket's case): keep full width, trim top and bottom.
        var height = imageWidth / slotAspect;
        var trim = (imageHeight - height) / 2f;
        return new SlotRect(0, trim, imageWidth, imageHeight - trim);
    }
}
