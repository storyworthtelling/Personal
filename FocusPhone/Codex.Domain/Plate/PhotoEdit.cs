namespace Codex.Domain.Plate;

/// <summary>The crop shapes offered under the window. Free is the whole frame.</summary>
public enum CropShape
{
    Free,

    /// <summary>1:1.</summary>
    Square,

    /// <summary>9:16 — the same real capture ratio the viewfinder opens on.</summary>
    Portrait,
}

/// <summary>A small, named set. Anything larger is a toy rather than a darkroom.</summary>
public enum PhotoFilter
{
    None,
    BlackAndWhite,
    Faded,
    Bright,
}

/// <summary>
/// A crop as fractions of the picture, so it survives whatever size the bitmap turns out to
/// be — the preview and the full-resolution original are not the same number of pixels.
/// </summary>
public readonly record struct CropBox(double Left, double Top, double Right, double Bottom)
{
    public static readonly CropBox Whole = new(0, 0, 1, 1);

    public bool IsWhole => this == Whole;
}

/// <summary>
/// What the owner has done to one picture, before any of it touches a bitmap.
///
/// <para>Crop, tint and filter are the one part of this phase with no existing analogue
/// anywhere in the suite — nothing to borrow, and so nothing already proven. That is exactly
/// why the arithmetic sits here, where the test suite reaches it, rather than inside an
/// Android bitmap call on a head that cannot be compiled in the sandbox.</para>
///
/// <para><b>The load-bearing rule is <see cref="IsUnchanged"/>.</b> A picture the owner did
/// not actually change must send its original bytes: re-encoding a JPEG for nothing loses
/// quality every single time, and every gesture here has a way of undoing itself — turning
/// four times, dragging the crop handles back to the edges, tinting back to the middle.
/// Each of those has to land back on "unchanged" or the loss happens anyway.</para>
/// </summary>
public sealed record PhotoEdit
{
    /// <summary>How far the tint goes in either direction.</summary>
    public const int TintSteps = 4;

    public static readonly PhotoEdit None = new();

    private PhotoEdit()
    {
    }

    /// <summary>Quarter turns clockwise, 0–3.</summary>
    public int QuarterTurns { get; private init; }

    public CropBox Crop { get; private init; } = CropBox.Whole;

    /// <summary>Cool to warm, zero being untouched.</summary>
    public int Tint { get; private init; }

    public PhotoFilter Filter { get; private init; } = PhotoFilter.None;

    /// <summary>Nothing was really done, so the original bytes go out untouched.</summary>
    public bool IsUnchanged =>
        QuarterTurns == 0 && Crop.IsWhole && Tint == 0 && Filter == PhotoFilter.None;

    public PhotoEdit Turn() => this with { QuarterTurns = (QuarterTurns + 1) % 4 };

    /// <summary>
    /// Takes a crop, clamped into the picture. A rect dragged past the edge would be a crash
    /// in the bitmap call rather than a wrong picture, and one turned inside out by a drag
    /// that crossed over itself would ask for a negative width — so that one is ignored.
    /// </summary>
    public PhotoEdit CropTo(CropBox box)
    {
        var left = Clamp(box.Left);
        var top = Clamp(box.Top);
        var right = Clamp(box.Right);
        var bottom = Clamp(box.Bottom);

        if (right <= left || bottom <= top)
        {
            return this;
        }

        return this with { Crop = new CropBox(left, top, right, bottom) };
    }

    public PhotoEdit TintBy(int steps) =>
        this with { Tint = Math.Clamp(Tint + steps, -TintSteps, TintSteps) };

    public PhotoEdit With(PhotoFilter filter) => this with { Filter = filter };

    /// <summary>
    /// The centred crop for a shape, on a picture of the given pixel size. Centred because a
    /// crop that jumped to a corner would move the subject the owner framed.
    /// </summary>
    public static CropBox Shaped(CropShape shape, int width, int height)
    {
        if (shape == CropShape.Free || width <= 0 || height <= 0)
        {
            return CropBox.Whole;
        }

        var wanted = shape == CropShape.Square ? 1.0 : 9.0 / 16.0;
        var actual = (double)width / height;

        if (actual > wanted)
        {
            // Too wide: take width away, keep all the height.
            var fraction = wanted / actual;
            var inset = (1 - fraction) / 2;
            return new CropBox(inset, 0, 1 - inset, 1);
        }

        var heightFraction = actual / wanted;
        var verticalInset = (1 - heightFraction) / 2;
        return new CropBox(0, verticalInset, 1, 1 - verticalInset);
    }

    /// <summary>What the segmented control calls each filter.</summary>
    public static string NameOf(PhotoFilter filter) => filter switch
    {
        PhotoFilter.BlackAndWhite => "Mono",
        PhotoFilter.Faded => "Faded",
        PhotoFilter.Bright => "Bright",
        _ => "None",
    };

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}
