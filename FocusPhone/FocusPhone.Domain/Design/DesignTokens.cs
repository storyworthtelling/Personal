namespace FocusPhone.Domain.Design;

/// <summary>
/// The Paper &amp; Ink design tokens, mirroring DESIGN.md at the repository root.
/// This is the only place in FocusPhone (besides the Ui.Android resource files
/// kept in parity by tests) where color and type-size literals may appear.
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

    /// <summary>Type scale in Android scaled pixels (sp).</summary>
    public static class TypeSp
    {
        public const float Clock = 64f;
        public const float Countdown = 40f;
        public const float Display = 34f;
        public const float Title = 24f;
        public const float Heading = 18f;
        public const float Body = 16f;
        public const float Secondary = 14f;
        public const float Label = 13f;
        public const float Mono = 14f;
    }

    /// <summary>Spacing scale in dp; every padding and margin is one of these.</summary>
    public static class Space
    {
        public const int X1 = 4;
        public const int X2 = 8;
        public const int X3 = 12;
        public const int X4 = 16;
        public const int X6 = 24;
        public const int X8 = 32;
        public const int X12 = 48;
        public const int X16 = 64;
        public const int X24 = 96;
    }

    public static class Shape
    {
        public const int RadiusInput = 12;
        public const int RadiusButton = 12;
        public const int RadiusCard = 12;
        public const int HairlineDp = 1;
        public const int WarningRuleDp = 3;
        public const int ListRowMinHeightDp = 56;
        public const int TouchTargetDp = 48;
        public const int ScreenEdgeDp = Space.X6;
    }

    /// <summary>Calm functional motion: two durations, decelerate curves, no haptics.</summary>
    public static class Motion
    {
        public const int QuickMs = 150;
        public const int StandardMs = 200;
        public const int UndoWindowMs = 6_000;
        public const int CutoverCountdownSeconds = 10;
    }

    /// <summary>Paper grain: deterministic seeded ink noise (DESIGN.md §6).</summary>
    public static class Grain
    {
        public const int TileSize = 256;
        public const int Alpha = 6;          // of 255 — ~2.4% ink
        public const int Density = 6;        // ~1 in Density pixels carries grain
        public const uint Seed = 1913;
    }

    /// <summary>Font asset paths as packaged by FocusPhone.Ui.Android.</summary>
    public static class FontAssets
    {
        public const string SerifRegular = "fonts/SourceSerif4-Regular.ttf";
        public const string SerifSemibold = "fonts/SourceSerif4-Semibold.ttf";
        public const string SerifItalic = "fonts/SourceSerif4-Italic.ttf";
        public const string SansRegular = "fonts/SourceSans3-Regular.ttf";
        public const string SansSemibold = "fonts/SourceSans3-Semibold.ttf";
        public const string Mono = "fonts/CascadiaCode-Regular.ttf";
    }
}
