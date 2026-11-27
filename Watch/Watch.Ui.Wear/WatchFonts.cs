using Android.Content;
using Android.Graphics;
using Watch.Domain.Design;

namespace Watch.Ui.Wear;

/// <summary>The three weights of IBM Plex Mono the mockups draw in (400 words, 500 labels, 600 names).</summary>
public enum WatchWeight
{
    Regular,
    Medium,
    SemiBold,
}

/// <summary>
/// IBM Plex Mono, the watch's one family (WD §5, O3), read from the app's own assets: an app that draws words carries
/// <c>Watch/Fonts</c> as <c>assets/fonts/</c> (Dash first, P14 B4), and the workflow's FONT CHECK reads each file back
/// out of the APK. An app that carries none (the installer, which draws nothing) is answered with the family by name,
/// which Android meets with its own default face.
/// </summary>
public static class WatchFonts
{
    private const string Folder = "fonts"; // not copy: the assets folder the apps link Watch/Fonts into

    private static readonly Dictionary<WatchWeight, Typeface> Loaded = [];

    private static readonly object Gate = new();

    /// <summary>The asset's file name for a weight, as <c>Watch/Fonts</c> names it.</summary>
    public static string FileOf(WatchWeight weight) => weight switch
    {
        WatchWeight.Medium => "IBMPlexMono-Medium.ttf", // not copy: a font file's name
        WatchWeight.SemiBold => "IBMPlexMono-SemiBold.ttf", // not copy: a font file's name
        _ => "IBMPlexMono-Regular.ttf", // not copy: a font file's name
    };

    public static Typeface Of(Context context, WatchWeight weight)
    {
        lock (Gate)
        {
            if (Loaded.TryGetValue(weight, out var known))
            {
                return known;
            }

            var assets = context.Assets;
            var carried = assets?.List(Folder)?.Contains(FileOf(weight), StringComparer.Ordinal) == true;
            var face = carried
                ? Typeface.CreateFromAsset(assets, $"{Folder}/{FileOf(weight)}")!
                : Typeface.Create(WatchTokens.Typeface, weight == WatchWeight.Regular ? TypefaceStyle.Normal : TypefaceStyle.Bold)!;
            Loaded[weight] = face;
            return face;
        }
    }
}
