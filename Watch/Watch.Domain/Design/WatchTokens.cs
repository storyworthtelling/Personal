namespace Watch.Domain.Design;

/// <summary>The four watch apps (WD §3). Each is its own shape and its own day colour.</summary>
public enum WatchApp
{
    Point,
    Dash,
    Wave,
    Beat,
}

/// <summary>An app's day range (WD §4.2): <see cref="Night"/> at midnight, <see cref="Day"/> at noon.</summary>
public readonly record struct DayRange(Oklch Night, Oklch Day);

/// <summary>
/// A haptic shape (WD §7): <see cref="Timings"/> alternate on, off, on … in milliseconds, starting
/// with an on; <see cref="Strengths"/> give each segment's strength, 0–255 (an off segment is 0).
/// </summary>
public sealed record HapticPattern(string Name, IReadOnlyList<int> Timings, IReadOnlyList<int> Strengths, bool Repeats);

/// <summary>
/// The watch's design language in one place (WD §13, P14 W-1.1.1): the day colour ranges, the three
/// type sizes, the four haptic shapes, the motion durations and the typeface. No other file in
/// <c>Watch/</c> may hold a colour or a type size (<c>WatchLeakGuardTests</c>).
/// </summary>
public static class WatchTokens
{
    /// <summary>The smallest touch target, in dp (WD §12).</summary>
    public const int TouchTargetDp = 48;

    /// <summary>IBM Plex Mono, the one family for words and numerals (WD §5, O3).</summary>
    public const string Typeface = "IBM Plex Mono";

    private static readonly DayRange PointRange = new(new Oklch(0.62, 0.19, 300), new Oklch(0.82, 0.10, 170));
    private static readonly DayRange DashRange = new(new Oklch(0.30, 0.10, 262), new Oklch(0.32, 0.12, 15));
    private static readonly DayRange WaveRange = new(new Oklch(0.62, 0.03, 250), new Oklch(0.80, 0.16, 75));
    private static readonly DayRange BeatRange = new(new Oklch(0.38, 0.09, 150), new Oklch(0.63, 0.24, 27));

    /// <summary>
    /// The app's day range: Point violet to seafoam, Dash navy to maroon, Wave slate to amber, Beat
    /// forest green to bright red (WD §4.2). Dark ends are drawn as named, with no lightness floor (O2).
    /// </summary>
    public static DayRange RangeOf(WatchApp app) => app switch
    {
        WatchApp.Point => PointRange,
        WatchApp.Dash => DashRange,
        WatchApp.Wave => WaveRange,
        WatchApp.Beat => BeatRange,
        _ => throw new ArgumentOutOfRangeException(nameof(app), app, "Not one of the four watch apps."),
    };

    /// <summary>
    /// How much lighter an app's glyph is drawn than its day colour (mock: Dash's glyphs and accents take
    /// +0.08 lightness so navy and maroon still read as glass on black). Every other app draws as named.
    /// </summary>
    public static double GlyphLift(WatchApp app) => app == WatchApp.Dash ? 0.08 : 0;

    /// <summary>The neutrals (WD §4.3): words, the quiet line, a ring's empty track. Cool greys, no colour.</summary>
    public static class Neutral
    {
        /// <summary>Primary words: a cool near-white.</summary>
        public static Oklch Ink { get; } = new(0.954, 0.006, 238.5);

        /// <summary>Secondary words, the quiet line, errors.</summary>
        public static Oklch Soft { get; } = new(0.715, 0.017, 238.7);

        /// <summary>A ring's empty track; never words.</summary>
        public static Oklch Faint { get; } = new(0.417, 0.016, 238.9);

        /// <summary>
        /// The card under a row or someone else's message (mock: CARD, hsl 210 10% 11%), as the system's own cards are
        /// drawn (WD §2): the one grey ground on the watch.
        /// </summary>
        public static Oklch Card { get; } = new(0.225, 0.007, 248.1);

        /// <summary>A pill's words on light glass (mock: hsl 0 0% 4%); on dark glass a pill takes the ink (WatchContrast).</summary>
        public static Oklch OnGlass { get; } = new(0.146, 0, 0);
    }

    /// <summary>How much light a part of a glass shape carries (mock: <c>waveLine</c>, <c>edgeMarks</c>), as alpha 0–1.</summary>
    public static class Light
    {
        /// <summary>The part of the wave not yet played.</summary>
        public const double Unplayed = 0.22;

        /// <summary>The wave's back and skip marks at rest.</summary>
        public const double EdgeMark = 0.5;

        /// <summary>A glyph with nothing behind it (mock: Tile: nobody waiting).</summary>
        public const double Quiet = 0.35;

        /// <summary>The owner's own message, in the app's quieter glass (mock: A conversation, the day colour at 0.35).</summary>
        public const double Mine = 0.35;
    }

    /// <summary>
    /// The three type sizes, in sp (WD §5). These are first values, to be settled on the wrist.
    /// </summary>
    public static class Type
    {
        /// <summary>A name or a time: the largest.</summary>
        public const float Glance = 20f;

        /// <summary>The one short sentence.</summary>
        public const float Line = 15f;

        /// <summary>Relative times and the date.</summary>
        public const float Small = 12f;
    }

    /// <summary>The four haptic shapes, one meaning each (WD §7, A35, O4); settled on the wrist.</summary>
    public static class Haptics
    {
        /// <summary>A call is ringing: long, gap, long, repeating while it rings.</summary>
        public static HapticPattern Call { get; } = new("Call", [500, 250, 500], [192, 0, 192], Repeats: true);

        /// <summary>A text arrived: two short taps.</summary>
        public static HapticPattern Text { get; } = new("Text", [80, 120, 80], [160, 0, 160], Repeats: false);

        /// <summary>A workout rest is over: one long press.</summary>
        public static HapticPattern RestEnd { get; } = new("RestEnd", [900], [192], Repeats: false);

        /// <summary>Worrying exertion: three slow pulses at full strength, the strongest shape.</summary>
        public static HapticPattern EaseOff { get; } = new("EaseOff", [300, 300, 300, 300, 300], [255, 0, 255, 0, 255], Repeats: false);

        public static IReadOnlyList<HapticPattern> All { get; } = [Call, Text, RestEnd, EaseOff];
    }

    /// <summary>True black, the ground of every watch screen (WD §2): an OLED pixel that is off.</summary>
    public static Oklch Ground { get; } = new(0, 0, 0);

    /// <summary>
    /// The face (WD §4.1, §10; study 1b). Each tone's hue is replaced by the minute's, so only lightness
    /// and chroma live here. <see cref="DateSize"/> is the face's one type size, as a fraction of its radius.
    /// </summary>
    public static class Face
    {
        /// <summary>Red in OKLCH: the hue at the top of the hour, so the wheel turns red, yellow, green, blue, violet (WD §4.1).</summary>
        public const double RedHue = 29.23;

        /// <summary>The lit shape's body (mock: hsl 75% 52%).</summary>
        public static Oklch Glass { get; } = new(0.66, 0.16, 0);

        /// <summary>The thin bright rim (mock: hsl 90% 85%).</summary>
        public static Oklch Rim { get; } = new(0.90, 0.08, 0);

        /// <summary>The after-noon ring (mock: hsl 85% 80%).</summary>
        public static Oklch Ring { get; } = new(0.86, 0.09, 0);

        /// <summary>The date, the shape's colour at low saturation (mock: hsl 18% 62%).</summary>
        public static Oklch Date { get; } = new(0.68, 0.035, 0);

        /// <summary>Always-on keeps about a third of the lit lightness (WD §2).</summary>
        public const double AlwaysOnShare = 1.0 / 3;

        /// <summary>Always-on's chroma: the hue still reads, quietly.</summary>
        public const double AlwaysOnChroma = 0.05;

        /// <summary>The after-noon ring's opacity (mock: 0.7).</summary>
        public const double RingAlpha = 0.7;

        /// <summary>The glass's highlight, laid over the body high on the left.</summary>
        public static Oklch Highlight { get; } = new(1, 0, 0);

        public const double HighlightAlpha = 0.5;

        /// <summary>The glass's deeper tone at the edge.</summary>
        public static Oklch Shade { get; } = new(0, 0, 0);

        public const double ShadeAlpha = 0.45;

        /// <summary>The date's size, as a fraction of the face's radius (mock: 0.08 R).</summary>
        public const double DateSize = 0.08;
    }

    /// <summary>Motion durations in milliseconds (WD §8). Reduced motion and always-on remove all of it.</summary>
    public static class Motion
    {
        /// <summary>A fade in or out.</summary>
        public const int FadeMs = 250;

        /// <summary>The face's shape changing on the hour.</summary>
        public const int HourCrossfadeMs = 1000;

        /// <summary>Wave's tempo crossfade, shortest (A21).</summary>
        public const int TempoCrossfadeMinMs = 5000;

        /// <summary>Wave's tempo crossfade, longest (A21).</summary>
        public const int TempoCrossfadeMaxMs = 6000;
    }
}
