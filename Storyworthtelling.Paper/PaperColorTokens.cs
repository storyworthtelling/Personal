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
    // The suite is Paper & Ink even on screen, so the Drive PDF / in-app ground is the paper cream, not
    // stark white (the mockup's white frame was only to distinguish the profiles visually). The two e-ink
    // grounds shift warmer/greyer for their panels.
    public const string PaperProGround = "#F4EFE3"; // warm colour e-ink
    public const string Rm2Ground = "#ECEAE4";      // grayscale e-ink
    public const string ScreenGround = Paper;       // Drive PDF / in-app — the paper cream

    // ================================================================================================
    // P4 design-system expansion — the lively "App" role model (DESIGN.md §3).
    // ------------------------------------------------------------------------------------------------
    // These are ADDITIVE: the base palette and the muted Sage/Slate/Ochre/Plum document set above are
    // unchanged, so current renderers keep working; apps adopt this model at their own rebrand phase.
    // Terracotta is retired for new work → the Action role is pine, on screen AND (muted) in documents.
    // Proposal values from the approved "Paper & Ink Controls" proof; the OKLCH generator will reproduce
    // them. Each role carries five tiers (wash/fill/mark/text/deep); wash is the mark at WashAlpha.
    // ================================================================================================

    /// <summary>"The pen" — signatures, editorial marks, and the colour of the owner's voice.</summary>
    public const string InkBlue = "#2C3E5E";

    /// <summary>Alpha (of 255) for the highlighter/wash tier — ~30%, ink over it still clears AA.</summary>
    public const int WashAlpha = 77;

    // ---- App profile (lively, on-screen): mark (T3) · text (T4) · deep (T5) · fill (T2) ----
    public const string ActionMark = "#347052"; public const string ActionText = "#2C6346"; public const string ActionDeep = "#235A3E"; public const string ActionFill = "#7FAB8E";
    public const string PositiveMark = "#5F8A34"; public const string PositiveText = "#4E7328"; public const string PositiveDeep = "#3B5820"; public const string PositiveFill = "#A0C07E";
    public const string ResolvedMark = "#8C8578"; public const string ResolvedText = "#6B6559"; public const string ResolvedDeep = "#4C4840"; public const string ResolvedFill = "#C9C4B6";
    public const string ReferenceMark = "#47638A"; public const string ReferenceText = "#354C6C"; public const string ReferenceDeep = "#25374E"; public const string ReferenceFill = "#93A5C0";
    public const string CautionMark = "#C09A34"; public const string CautionText = "#83641A"; public const string CautionDeep = "#6A5115"; public const string CautionFill = "#E2CD86";
    public const string ProblemMark = "#BE5560"; public const string ProblemText = "#9A3D47"; public const string ProblemDeep = "#6E2931"; public const string ProblemFill = "#E0A6AC";
    public const string ProgressMark = "#2E8086"; public const string ProgressText = "#256268"; public const string ProgressDeep = "#1E4C50"; public const string ProgressFill = "#86B7BC";

    // ---- Document profile (muted, Paper Pro colour e-ink / Drive PDF): one hue per role ----
    public const string ActionDoc = "#4F6B57";   // muted pine (replaces terracotta in documents)
    public const string PositiveDoc = "#6E7A53"; // = Sage
    public const string ResolvedDoc = "#8A857A"; // warm gray
    public const string ReferenceDoc = "#4E6273";// = Slate
    public const string CautionDoc = "#B08A3E";  // = Ochre
    public const string ProblemDoc = "#8A4A55";  // muted rose
    public const string ProgressDoc = "#4E7370"; // muted teal

    // ---- Document grayscale ramp (rM2): distinct, monotonic, separable (all true greys) ----
    // Problem 4A → Action 5E → InProgress 6E → Reference 76 → Resolved 88 → Positive 9A → Caution A8.
    public const string ProblemGray = "#4A4A4A";
    public const string ActionGray = "#5E5E5E";
    public const string ProgressGray = "#6E6E6E";
    public const string ReferenceGray = "#767676";
    public const string ResolvedGray = "#888888";
    public const string PositiveGray = "#9A9A9A";
    public const string CautionGray = "#A8A8A8";

    /// <summary>Paper grain: deterministic seeded ink noise (DESIGN.md §6). Identical inputs → identical bytes.</summary>
    public static class Grain
    {
        public const int TileSize = 256;
        public const int Alpha = 6;    // of 255 — ~2.4% ink
        public const int Density = 6;  // ~1 in Density pixels carries grain
        public const uint Seed = 1913;
    }
}
