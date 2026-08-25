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

    public static readonly SKColor FlyleafWarm = SKColor.Parse(FlyleafWarmHex);
    public static readonly SKColor FlyleafMark = SKColor.Parse(FlyleafMarkHex);
    public static readonly SKColor FlyleafDeep = SKColor.Parse(FlyleafDeepHex);
}
