namespace Codex.Domain.Plate;

/// <summary>Which corner of a free crop a drag is moving.</summary>
public enum CropHandle
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// The touch math behind Free crop's drag handles, kept out of the Android head so it can be
/// tested without a device. Positions and the touch radius are pixel coordinates in whatever
/// space the caller is already measuring the picture in, paired with that picture's own
/// width/height — so "which corner is nearest" respects the picture's aspect ratio instead of
/// treating a landscape photo as square. <see cref="PhotoEdit.CropTo"/> stays the one place a
/// resulting box is judged valid; this only says where a handle lands, never whether it may.
/// </summary>
public static class CropGesture
{
    /// <summary>
    /// The handle nearest (x, y), or null when nothing is within <paramref name="radius"/>
    /// pixels of it — a touch in the middle of the picture should drag nothing.
    /// </summary>
    public static CropHandle? Nearest(
        CropBox box, double width, double height, double x, double y, double radius)
    {
        var corners = new (CropHandle Handle, double X, double Y)[]
        {
            (CropHandle.TopLeft, box.Left * width, box.Top * height),
            (CropHandle.TopRight, box.Right * width, box.Top * height),
            (CropHandle.BottomLeft, box.Left * width, box.Bottom * height),
            (CropHandle.BottomRight, box.Right * width, box.Bottom * height),
        };

        CropHandle? nearest = null;
        var closest = double.MaxValue;
        foreach (var (handle, cornerX, cornerY) in corners)
        {
            var dx = cornerX - x;
            var dy = cornerY - y;
            var distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared < closest)
            {
                closest = distanceSquared;
                nearest = handle;
            }
        }

        return closest <= radius * radius ? nearest : null;
    }

    /// <summary>
    /// The box with <paramref name="handle"/> dragged to (x, y), expressed as a fraction of
    /// the picture. The result can be inverted or run past the edge — a caller routes it
    /// through <see cref="PhotoEdit.CropTo"/> rather than checking it here, so there stays
    /// exactly one place a crop box is decided valid.
    /// </summary>
    public static CropBox Moved(
        CropBox box, CropHandle handle, double width, double height, double x, double y)
    {
        var fractionX = width <= 0 ? 0 : x / width;
        var fractionY = height <= 0 ? 0 : y / height;

        return handle switch
        {
            CropHandle.TopLeft => box with { Left = fractionX, Top = fractionY },
            CropHandle.TopRight => box with { Right = fractionX, Top = fractionY },
            CropHandle.BottomLeft => box with { Left = fractionX, Bottom = fractionY },
            _ => box with { Right = fractionX, Bottom = fractionY },
        };
    }
}
