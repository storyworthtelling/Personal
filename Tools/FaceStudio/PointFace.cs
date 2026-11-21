namespace FaceStudio;

/// <summary>
/// Point, the watch face (P14 §2.2, §4). The package id is frozen: a watch recognises an update only
/// under the same name, so it is a string literal here and nowhere else, never built from a name.
/// </summary>
public static class PointFace
{
    /// <summary>The face's own package (P14 §2.2): it needs no phone partner.</summary>
    public const string PackageId = "com.storyworthtelling.watch.point";

    /// <summary>
    /// Watch Face Format version 4, the first that can change a colour with an expression (the minute's
    /// hue, WD §4.1). Wear OS 6 runs it, and both watches are on Wear OS 6 (TF §2).
    /// </summary>
    public const int FormatVersion = 4;

    /// <summary>Wear OS 6 is Android 16, API 36.</summary>
    public const int MinSdk = 36;

    /// <summary>The face's frame in Watch Face Format units; the watch scales it to its screen.</summary>
    public const int Canvas = 450;

    /// <summary>The name in the watch's own settings and face picker (A48).</summary>
    public const string Label = "Point";

    /// <summary>Who made it, as the format's publisher property asks.</summary>
    public const string Publisher = "FaceStudio";

    /// <summary>The date's face: IBM Plex Mono Medium (A57), the watch's own copy in <c>Watch/Fonts</c>.</summary>
    public const string FontFile = "IBMPlexMono-Medium.ttf";

    /// <summary>Its resource name in <c>res/font</c>, which the format's <c>Font family</c> names.</summary>
    public const string FontResource = "plexmono";

    /// <summary>The time the face picker's preview shows (an invented afternoon: four sides and the ring).</summary>
    public static TimeOnly PreviewTime { get; } = new(16, 20);
}
