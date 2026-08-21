using SkiaSharp;

namespace IconStudio;

/// <summary>
/// The Paper &amp; Ink palette as used by the icon family. Mirrors DESIGN.md at
/// the repository root; IconStudio.Tests keeps these values honest.
/// </summary>
public static class Tokens
{
    public const string PaperHex = "#FAF8F2";
    public const string InkHex = "#3A3A33";
    public const string AccentHex = "#C2542F";
    public const string HairlineHex = "#E3DCC9";

    // Flyleaf's identity (warm study amber) — its icon is a cream bookplate on this warm ground.
    public const string IdentityWarmHex = "#C9803A";
    public const string IdentityMarkHex = "#B4552F";
    public const string IdentityDeepHex = "#8A4A22";

    public static readonly SKColor Paper = SKColor.Parse(PaperHex);
    public static readonly SKColor Ink = SKColor.Parse(InkHex);
    public static readonly SKColor Accent = SKColor.Parse(AccentHex);
    public static readonly SKColor Hairline = SKColor.Parse(HairlineHex);
    public static readonly SKColor IdentityWarm = SKColor.Parse(IdentityWarmHex);
    public static readonly SKColor IdentityMark = SKColor.Parse(IdentityMarkHex);
    public static readonly SKColor IdentityDeep = SKColor.Parse(IdentityDeepHex);
}
