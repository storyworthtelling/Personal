namespace Codex.Domain.Plate;

/// <summary>The shape of the frame. Both entries are real sensor readouts, never a crop.</summary>
public enum CaptureRatio
{
    /// <summary>The full 4:3 readout, in portrait.</summary>
    ThreeFour,

    /// <summary>The 16:9 crop, in portrait.</summary>
    NineSixteen,
}

/// <summary>Which camera. Front is for the owner; back is for everything else.</summary>
public enum Lens
{
    Back,
    Front,
}

/// <summary>The three states of one control, in the order it cycles.</summary>
public enum Flash
{
    Off,
    On,
    Auto,
}

/// <summary>A window's proportions, in the smallest whole numbers that describe them.</summary>
public readonly record struct WindowRatio(int Width, int Height);

/// <summary>
/// The camera's quiet controls, and the one rule that governs the viewfinder.
///
/// <para><b>The window always carries the ratio the sensor is actually capturing.</b> It is
/// never letterboxed inside itself, never stretched, and never cropped to fill the space
/// under the controls — if the owner switches to 3:4 the window gets <i>shorter</i> and the
/// empty space comes back, because the alternative is a preview that lies about the frame
/// being composed. The rule lives here rather than in the Activity so it can be held to
/// without a phone in the room.</para>
/// </summary>
public static class CaptureFormat
{
    /// <summary>
    /// What the camera opens on. At this screen width a 3:4 window leaves roughly 210dp of
    /// dead space under the controls; 9:16 leaves about 78dp and is still a real capture
    /// ratio, which is the only reason it is allowed to be the default.
    /// </summary>
    public const CaptureRatio Default = CaptureRatio.NineSixteen;

    public static WindowRatio Window(CaptureRatio ratio) => ratio switch
    {
        CaptureRatio.ThreeFour => new WindowRatio(3, 4),
        CaptureRatio.NineSixteen => new WindowRatio(9, 16),
        _ => throw new ArgumentOutOfRangeException(nameof(ratio), ratio,
            "A capture ratio with no window would have to be produced by cropping the preview."),
    };

    public static CaptureRatio NextRatio(CaptureRatio from) =>
        from == CaptureRatio.ThreeFour ? CaptureRatio.NineSixteen : CaptureRatio.ThreeFour;

    public static Lens NextLens(Lens from) => from == Lens.Back ? Lens.Front : Lens.Back;

    /// <summary>
    /// One control, three states, in a fixed order — so the owner can get back to where they
    /// were by tapping rather than by hunting through a menu.
    /// </summary>
    public static Flash NextFlash(Flash from) => from switch
    {
        Flash.Off => Flash.On,
        Flash.On => Flash.Auto,
        _ => Flash.Off,
    };

    /// <summary>
    /// The phone has no front-facing flash. Offering the control there would be a button
    /// that does nothing, which is worse than one that is not there.
    /// </summary>
    public static bool HasFlash(Lens lens) => lens == Lens.Back;
}
