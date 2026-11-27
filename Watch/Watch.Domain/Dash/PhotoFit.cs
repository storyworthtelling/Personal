namespace Watch.Domain.Dash;

/// <summary>
/// How a photo is shrunk on the phone before it crosses to the wrist (P14 B5, W-5.1.3). The wrist draws it in the
/// mockup's picture, a box 1.0 by 0.6 of the radius (at most 228 by 137 pixels on the larger watch), so a shorter side of
/// <see cref="ShortSide"/> fills the box whatever the photo's shape, and <see cref="LongSide"/> bounds a panorama. It is
/// never enlarged, its shape is kept, and it is written as a JPEG at the first of <see cref="Qualities"/> that fits
/// <see cref="DashLimits.PhotoBytes"/>; if none does, each smaller size in turn (<see cref="Sizes"/>). Re-encoding also
/// leaves the picture's own metadata, where and when it was taken, on the phone. The figures are chosen, not measured.
/// </summary>
public static class PhotoFit
{
    /// <summary>The most a photo's shorter side may be, in pixels.</summary>
    public const int ShortSide = 240;

    /// <summary>The most a photo's longer side may be, in pixels.</summary>
    public const int LongSide = 640;

    /// <summary>A photo whose shorter side would be smaller than this is not worth sending.</summary>
    public const int SmallestSide = 60;

    /// <summary>The JPEG qualities tried, best first.</summary>
    public static readonly IReadOnlyList<int> Qualities = [80, 70, 60, 50, 40];

    /// <summary>The size to send a photo of <paramref name="width"/> by <paramref name="height"/> pixels at.</summary>
    public static (int Width, int Height) Size(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        var scale = Math.Min(1.0, Math.Min((double)ShortSide / Math.Min(width, height), (double)LongSide / Math.Max(width, height)));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>Each size to try in turn: the fitted one, then half of it each time while its shorter side stays at least <see cref="SmallestSide"/>.</summary>
    public static IReadOnlyList<(int Width, int Height)> Sizes(int width, int height)
    {
        var size = Size(width, height);
        var sizes = new List<(int Width, int Height)> { size };
        while (Math.Min(size.Width, size.Height) / 2 >= SmallestSide)
        {
            size = (size.Width / 2, size.Height / 2);
            sizes.Add(size);
        }
        return sizes;
    }

    /// <summary>The largest power of two to decode the photo at that still leaves it no smaller than its fitted size.</summary>
    public static int SampleFor(int width, int height)
    {
        var (fittedWidth, fittedHeight) = Size(width, height);
        var sample = 1;
        while (width / (sample * 2) >= fittedWidth && height / (sample * 2) >= fittedHeight)
        {
            sample *= 2;
        }
        return sample;
    }

    /// <summary>
    /// The turn, in degrees clockwise, that stands a photo upright from its EXIF orientation (1 to 8); a mirrored one is
    /// turned and left mirrored, and an unknown one is left as it is.
    /// </summary>
    public static int TurnFor(int orientation) => orientation switch
    {
        3 or 4 => 180,
        5 or 6 => 90,
        7 or 8 => 270,
        _ => 0,
    };

    /// <summary>Whether a JPEG of <paramref name="length"/> bytes can cross as one message.</summary>
    public static bool Fits(int length) => length is > 0 and <= DashLimits.PhotoBytes;

    /// <summary>Whether the bytes open as a JPEG does: its start-of-image marker and the first byte of the marker after it.</summary>
    public static bool IsJpeg(byte[] bytes) => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
}
