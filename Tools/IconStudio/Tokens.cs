using SkiaSharp;

namespace IconStudio;

/// <summary>
/// The Paper &amp; Ink palette as used by the icon family. Mirrors DESIGN.md at
/// the repository root; IconStudio.Tests keeps these values honest.
/// </summary>
public static class Tokens
{
    public const string PaperHex = "#FAF8F2";
    public const string InkHex = "#33322B";
    public const string AccentHex = "#C2542F";
    public const string HairlineHex = "#E3DCC9";

    // Flyleaf's identity (warm study amber) — its icon is a cream bookplate on this warm ground.
    public const string FlyleafWarmHex = "#C9803A";
    public const string FlyleafMarkHex = "#B4552F";
    public const string FlyleafDeepHex = "#8A4A22";

    public static readonly SKColor Paper = SKColor.Parse(PaperHex);
    public static readonly SKColor Ink = SKColor.Parse(InkHex);
    public static readonly SKColor Accent = SKColor.Parse(AccentHex);
    public static readonly SKColor Hairline = SKColor.Parse(HairlineHex);
    // Score's identity (teal-slate) — its icon is a keyboard on this ground.
    public const string ScoreGroundHex = "#93ABA5";
    public const string ScoreMarkHex = "#436A61";
    public const string ScoreDeepHex = "#263B36";

    public static readonly SKColor ScoreGround = SKColor.Parse(ScoreGroundHex);

    // Post's identity (deep indigo, sampled from the cover's BINDING rather than its warm
    // mailbox painting — see DESIGN.md §3). Its icon is that cover: the indigo cloth as the
    // ground, with the painting set into a window on the foreground.
    public const string PostGroundHex = "#A1A1C7";
    public const string PostMarkHex = "#5C5894";
    public const string PostDeepHex = "#333252";

    // Foreword's identity: sunrise, OKLCH hue 47°, locked at gate 3. The icon is its cover —
    // the engraved doorstep set into a window on this ground. It sits between Flyleaf's warm
    // 38.6° and the accent terracotta, which is deliberate: the two apps are neighbours in the
    // morning and their covers were chosen to read as the same library.
    public const string ForewordGroundHex = "#DFA277";
    public const string ForewordSunriseHex = "#CA662D";
    public const string ForewordDeepHex = "#6E3512";

    public static readonly SKColor ForewordGround = SKColor.Parse(ForewordGroundHex);
    public static readonly SKColor ForewordSunrise = SKColor.Parse(ForewordSunriseHex);
    public static readonly SKColor ForewordDeep = SKColor.Parse(ForewordDeepHex);

    public static readonly SKColor PostGround = SKColor.Parse(PostGroundHex);
    public static readonly SKColor PostMark = SKColor.Parse(PostMarkHex);
    public static readonly SKColor PostDeep = SKColor.Parse(PostDeepHex);

    public static readonly SKColor FlyleafWarm = SKColor.Parse(FlyleafWarmHex);
    public static readonly SKColor FlyleafMark = SKColor.Parse(FlyleafMarkHex);
    public static readonly SKColor FlyleafDeep = SKColor.Parse(FlyleafDeepHex);
}
