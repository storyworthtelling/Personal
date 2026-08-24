namespace Storyboarder.Domain.Design;

/// <summary>
/// The Paper &amp; Ink design tokens, mirroring DESIGN.md at the repository root.
/// The only sources of design literals in Storyboarder are this class, the
/// XAML dictionary kept in parity with it by tests, and generated icon
/// resources. Colors are hex strings so both MAUI and SkiaSharp can parse them.
/// </summary>
public static class DesignTokens
{
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

    /// <summary>In-app type scale (MAUI font sizes).</summary>
    public static class Type
    {
        public const double Display = 34;
        public const double Title = 24;
        public const double Heading = 18;
        public const double Body = 16;
        public const double Secondary = 14;
        public const double Label = 13;
        public const double Mono = 14;
    }

    /// <summary>
    /// Comic volume pages (px at the composed page size). Rendered at 2048×3072 (2:3) so the pages
    /// carry enough resolution for print: at <see cref="PrintDpi"/> that is ~6.8×10.2in, and each
    /// panel occupies enough pixels that the ~1MP image-model output fills it without visible softening.
    /// Every length here scales together, so the page keeps the same proportions at any size.
    /// </summary>
    public static class Comic
    {
        public const int PageWidth = 2_048;
        public const int PageHeight = 3_072;
        public const int TopMargin = 128;
        public const int SideMargin = 128;
        public const int BottomMargin = 232; // contains the folio band
        public const int FolioBand = 104;
        public const int Gutter = 48;
        public const float PanelStroke = 4f;
        public const float BoxStroke = 3f;
        public const float LetteringSize = 34f;  // dialogue, Sans
        public const float CaptionSize = 32f;    // captions, Serif Italic
        public const float SfxSize = 96f;        // sound-effect display lettering over the art, Serif Semibold
        public const float FolioSize = 36f;      // page number + running title, Serif
        public const float BalloonRadius = 28f;
        public const float CaptionRadius = 20f;
        public const float BoxPadding = 16f;
        public const float LineSpacing = 1.35f;
        public const float CoverTitleSize = 144f;
        public const float CoverMedallionSize = 240f;

        // The physical resolution the volume PDF declares for its full-page rasters, so print output
        // is sized for print (≈300 DPI) rather than PdfSharp's default 96 DPI screen assumption.
        public const int PrintDpi = 300;
    }

    /// <summary>Analysis report pages (px at A4 150 DPI).</summary>
    public static class Pdf
    {
        public const int PageWidth = 1_240;
        public const int PageHeight = 1_754;

        // A4 at this resolution is exactly 1240×1754 px, so the report PDF declares 150 DPI and prints
        // at true A4 rather than PdfSharp's default 96 DPI (which would oversize the page).
        public const int PrintDpi = 150;
        public const float SideMargin = 110f;
        public const float TopMargin = 100f;
        public const float BottomMargin = 130f; // contains the footer band
        public const float H1 = 58f;
        public const float H2 = 40f;
        public const float H3 = 32f;
        public const float Body = 30f;
        public const float Footer = 22f;
        public const float LineSpacing = 1.35f;
        public const float BulletIndent = 34f;
        public const float AccentRuleHeight = 2f;
        public const float AccentRuleGap = 24f;
        public const float CoverTitle = 72f;
        public const float CoverMedallionSize = 160f;

        // The per-scene evidence image block in the report: portrait (it sits in a column beside the
        // prose), letterboxed on shaded paper.
        public const float SceneImageWidth = 560f;
        public const float SceneImageHeight = 747f;

        // The tension-over-chapters pacing chart.
        public const float PacingChartHeight = 220f;
    }

    /// <summary>Calm functional motion: two durations, decelerate curves, no haptics.</summary>
    public static class Motion
    {
        public const int QuickMs = 150;
        public const int StandardMs = 200;
        public const int UndoWindowMs = 6_000;
    }

    /// <summary>Paper grain: deterministic seeded ink noise (DESIGN.md §6).</summary>
    public static class Grain
    {
        public const int TileSize = 256;
        public const int Alpha = 6;          // of 255 — ~2.4% ink
        public const int Density = 6;        // ~1 in Density pixels carries grain
        public const uint Seed = 1913;
    }
}
