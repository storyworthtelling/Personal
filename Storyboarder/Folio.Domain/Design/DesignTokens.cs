namespace Folio.Domain.Design;

/// <summary>
/// What is left of this head's own design literals, and why each piece is still here.
///
/// <para>The <b>screens</b> no longer read any of it. P10 Release C deleted this head's XAML
/// dictionary and its in-app type ladder, and the app now takes both from the shared
/// <c>Storyworthtelling.Maui</c> kit — one palette, enforced by reflection against
/// <c>PaperColorTokens</c> rather than by a second hand-typed copy that could be wrong in the same
/// way.</para>
///
/// <para>What remains is the <b>printed</b> side, which the kit cannot serve: the kit targets
/// <c>net10.0-android</c>, and the renderers that need these values live in Infrastructure, which
/// also builds for the desktop framework the tests run on. <see cref="Pdf"/> is the report page
/// geometry and the colours above it are what <c>CoverPageRenderer</c> draws with. Both move to
/// <c>Storyworthtelling.Paper</c>'s Document profile in Release E, which is also where
/// <see cref="Accent"/> stops being the retired terracotta.</para>
///
/// <para><see cref="Motion"/> and <see cref="Grain"/> are recorded DESIGN.md values that nothing in
/// this app reads yet. Left rather than deleted: they are facts, not duplicates of the kit.</para>
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
        public const int Alpha = 32;         // of 255 — ~12.5% ink
        public const int Density = 6;        // ~1 in Density pixels carries grain
        public const uint Seed = 1913;
    }
}
