namespace Ledger.Domain.Design;

/// <summary>
/// The Paper &amp; Ink design tokens, mirroring DESIGN.md at the repository root.
/// The only sources of design literals in Ledger are this class, the XAML
/// dictionary kept in parity with it by tests, and generated icon resources.
/// Colors are hex strings so both MAUI and SkiaSharp can parse them.
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

    /// <summary>Calm functional motion: two durations, decelerate curves, no haptics.</summary>
    public static class Motion
    {
        public const int QuickMs = 150;
        public const int StandardMs = 200;
        public const int UndoWindowMs = 6_000;
    }
}
