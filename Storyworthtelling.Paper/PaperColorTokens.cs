namespace Storyworthtelling.Paper;

/// <summary>
/// The Paper &amp; Ink colour tokens for the shared composition library — the base monochrome-plus-accent
/// palette (mirroring DESIGN.md), the four calm colour-e-ink hues added for the reMarkable Paper Pro, and
/// their designed grayscale ramp for the rM2. This is a <b>registered token file</b> (DESIGN.md §16): it is
/// the only place in this library allowed to carry colour-hex literals, and a leak-guard test enforces that.
/// Colours are hex strings so both SkiaSharp and any XAML consumer can parse them.
/// </summary>
public static class PaperColorTokens
{
    // ---- Base palette (unchanged from DESIGN.md; additive work never touches these) ----
    public const string Paper = "#FAF8F2";
    public const string PaperBright = "#FFFFFF";
    public const string PaperDeep = "#F1EBDD";
    public const string PaperShade = "#E9E1CF";
    public const string Hairline = "#E3DCC9";
    public const string Ink = "#3A3A33";
    public const string InkSoft = "#6B6759";
    public const string InkMuted = "#8A8578";
    public const string Accent = "#C2542F";
    public const string AccentDeep = "#9C4023";

    // ---- Calm colour-e-ink hues (approved P3 mockup — Paper Pro / screen) ----
    // Chosen low-saturation on purpose: the Paper Pro's Kaleido e-ink can't show vivid colour, and calm is
    // the acceptance test. Each carries a distinct grayscale value below and a non-colour cue in the renderer.
    public const string Sage = "#6E7A53";   // positive / new / resolved
    public const string Slate = "#4E6273";  // structure / prior draft / reference
    public const string Ochre = "#B08A3E";  // caution / consider
    public const string Plum = "#7A4E63";   // problem / regressed

    // ---- Designed grayscale ramp (rM2) ----
    // A deliberate ramp (#46 → #A2), NOT the naive luminance of each hue (several of which would collide),
    // so the five semantic colours stay separable side by side after desaturation.
    public const string GrayPlum = "#464646";   // darkest
    public const string GrayAccent = "#5E5E5E";
    public const string GraySlate = "#767676";
    public const string GraySage = "#8C8C8C";
    public const string GrayOchre = "#A2A2A2";  // lightest

    // ---- Device grounds (paper colour per profile) ----
    public const string PaperProGround = "#F4EFE3"; // warm colour e-ink
    public const string Rm2Ground = "#ECEAE4";      // grayscale e-ink
    public const string ScreenGround = "#FFFFFF";   // Drive PDF / in-app

    /// <summary>Paper grain: deterministic seeded ink noise (DESIGN.md §6). Identical inputs → identical bytes.</summary>
    public static class Grain
    {
        public const int TileSize = 256;
        public const int Alpha = 6;    // of 255 — ~2.4% ink
        public const int Density = 6;  // ~1 in Density pixels carries grain
        public const uint Seed = 1913;
    }
}
