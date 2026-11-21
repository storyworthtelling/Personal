using Watch.Domain.Design;

namespace Watch.Domain.Face;

/// <summary>
/// The face's colours at a hue (WD §4.1, §10): the minute gives the hue, the tone gives a fixed
/// lightness and chroma from <see cref="WatchTokens.Face"/>, so a shape is equally bright at every minute.
/// The minute's hue (0 at the top of the hour) is turned by <see cref="WatchTokens.Face.RedHue"/>, so the
/// hour starts on red rather than on OKLCH's own zero, a pink.
/// </summary>
public static class FaceColour
{
    private const double FullTurn = 360;

    /// <summary>The body of the lit glass shape.</summary>
    public static Oklch Glass(double hue) => WatchTokens.Face.Glass with { H = Wheel(hue) };

    /// <summary>The thin bright rim round a lit shape, and two o'clock's line.</summary>
    public static Oklch Rim(double hue) => WatchTokens.Face.Rim with { H = Wheel(hue) };

    /// <summary>The after-noon ring, drawn at <see cref="WatchTokens.Face.RingAlpha"/>.</summary>
    public static Oklch Ring(double hue) => WatchTokens.Face.Ring with { H = Wheel(hue) };

    /// <summary>The date: the shape's colour at low saturation.</summary>
    public static Oklch Date(double hue) => WatchTokens.Face.Date with { H = Wheel(hue) };

    /// <summary>The always-on outline: a third of the lit rim's lightness (WD §2, §10).</summary>
    public static Oklch AlwaysOn(double hue) =>
        new(WatchTokens.Face.Rim.L * WatchTokens.Face.AlwaysOnShare, WatchTokens.Face.AlwaysOnChroma, Wheel(hue));

    /// <summary>The always-on after-noon ring: a third of the lit ring's lightness.</summary>
    public static Oklch AlwaysOnRing(double hue) =>
        new(WatchTokens.Face.Ring.L * WatchTokens.Face.AlwaysOnShare, WatchTokens.Face.AlwaysOnChroma, Wheel(hue));

    /// <summary>The minute's hue turned to start on red, kept on the wheel.</summary>
    private static double Wheel(double hue)
    {
        var turned = (hue + WatchTokens.Face.RedHue) % FullTurn;
        return turned < 0 ? turned + FullTurn : turned;
    }
}
