namespace Storyworthtelling.Maui;

/// <summary>
/// The dp scales the MAUI kit lays out against — spacing and shape, copied from
/// <c>Codex.Domain.Design.DesignTokens</c> and kept identical by a parity guard.
///
/// <para>It is a copy rather than a reference because <c>Codex.Domain</c> is FocusPhone's and is
/// deliberately dependency-free, which is the same reason <c>RelativeTime</c> and
/// <c>TimeFormats</c> exist twice in this repository. The guard is what makes the duplication
/// honest: it compares the two sources rather than trusting anyone to remember.</para>
///
/// <para><b>Type sizes are not here.</b> They live in <c>Resources/Styles/Paper.xaml</c>, which is
/// the registered token file (DESIGN.md §19); a second home for them is exactly the split this kit
/// was built to close.</para>
/// </summary>
public static class DesignTokens
{
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

        /// <summary>
        /// The checkbox's drawn size (DESIGN.md §7). Small on purpose — §7 says
        /// refined controls keep a small visual and borrow a ≥48dp hit-area, which
        /// here is the 56dp list row it sits in.
        /// </summary>
        public const int CheckboxDp = 20;
        public const int TouchTargetDp = 48;
        public const int ScreenEdgeDp = Space.X6;
    }
}
