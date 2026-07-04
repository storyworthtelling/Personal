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
    public const string Ink = "#3A3A33";
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

    /// <summary>Comic volume pages (px at the composed page size).</summary>
    public static class Comic
    {
        public const int PageWidth = 1_024;
        public const int PageHeight = 1_536;
        public const int TopMargin = 64;
        public const int SideMargin = 64;
        public const int BottomMargin = 116; // contains the folio band
        public const int FolioBand = 52;
        public const int Gutter = 24;
        public const float PanelStroke = 2f;
        public const float BoxStroke = 1.5f;
        public const float LetteringSize = 17f;  // dialogue, Sans
        public const float CaptionSize = 16f;    // captions, Serif Italic
        public const float FolioSize = 18f;      // page number + running title, Serif
        public const float BalloonRadius = 8f;
        public const float CaptionRadius = 4f;
        public const float BoxPadding = 8f;
        public const float LineSpacing = 1.35f;
    }

    /// <summary>Analysis report pages (px at ~A4 150 DPI).</summary>
    public static class Pdf
    {
        public const int PageWidth = 1_240;
        public const int PageHeight = 1_754;
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
    }
}
