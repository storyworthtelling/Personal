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
    public const string Ink = "#33322B";
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

    // Values below are GENERATED from the OKLCH source spec (PaletteSpec + Oklch); the
    // GeneratedPaletteMatchesCommittedTokens test regenerates and asserts equality, so they are never
    // hand-edited. To change the palette, change the spec and re-sync these.

    // ---- App profile (lively, on-screen): mark (T3) · text (T4) · deep (T5) · fill (T2) ----
    public const string ActionMark = "#38734A"; public const string ActionText = "#295E3A"; public const string ActionDeep = "#174025"; public const string ActionFill = "#A5C8AD";
    public const string PositiveMark = "#517025"; public const string PositiveText = "#405B18"; public const string PositiveDeep = "#293E08"; public const string PositiveFill = "#B1C69A";
    public const string ResolvedMark = "#68645E"; public const string ResolvedText = "#54514B"; public const string ResolvedDeep = "#393632"; public const string ResolvedFill = "#C0BDB9";
    public const string ReferenceMark = "#4C6787"; public const string ReferenceText = "#3B536F"; public const string ReferenceDeep = "#26384E"; public const string ReferenceFill = "#AEC0D5";
    public const string CautionMark = "#805F00"; public const string CautionText = "#694C00"; public const string CautionDeep = "#493200"; public const string CautionFill = "#D0BB91";
    public const string ProblemMark = "#9E434B"; public const string ProblemText = "#82333B"; public const string ProblemDeep = "#5C1F26"; public const string ProblemFill = "#E8ACAD";
    public const string ProgressMark = "#18717C"; public const string ProgressText = "#085C65"; public const string ProgressDeep = "#003F46"; public const string ProgressFill = "#9BC7CD";

    // ---- Document profile (muted, Paper Pro colour e-ink / Drive PDF): one hue per role ----
    public const string ActionDoc = "#52725A";   // muted pine (replaces terracotta in documents)
    public const string PositiveDoc = "#5D7048";
    public const string ResolvedDoc = "#6B6965";
    public const string ReferenceDoc = "#5B6B7E";
    public const string CautionDoc = "#79663F";
    public const string ProblemDoc = "#8D585A";
    public const string ProgressDoc = "#477177";

    // ---- Document grayscale ramp (rM2): distinct, monotonic, separable (all true greys) ----
    // Problem 3D → Action 52 → InProgress 63 → Reference 70 → Resolved 82 → Positive 95 → Caution A4.
    public const string ProblemGray = "#3D3D3D";
    public const string ActionGray = "#525252";
    public const string ProgressGray = "#636363";
    public const string ReferenceGray = "#707070";
    public const string ResolvedGray = "#828282";
    public const string PositiveGray = "#959595";
    public const string CautionGray = "#A4A4A4";

    /// <summary>Paper grain: deterministic seeded ink noise (DESIGN.md §6). Identical inputs → identical bytes.</summary>
    public static class Grain
    {
        public const int TileSize = 256;
        public const int Alpha = 6;    // of 255 — ~2.4% ink
        public const int Density = 6;  // ~1 in Density pixels carries grain
        public const uint Seed = 1913;
    }
}
