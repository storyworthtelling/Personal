namespace Codex.Domain.Design;

/// <summary>
/// The Paper &amp; Ink design tokens (P4 role model), mirroring DESIGN.md at the
/// repository root. This is the only place in Codex (besides the Ui.Android
/// resource files kept in parity by tests) where color and type-size literals may
/// appear. The neutral + role values re-export Storyworthtelling.Paper's generated
/// PaperColorTokens; PaperReExportParityTests asserts they stay in lockstep, so
/// Paper remains the single source of truth without the pure Domain taking a
/// runtime reference on its Skia-carrying assembly. Terracotta is retired — the app
/// accent is now the Action role (pine).
/// </summary>
public static class DesignTokens
{
    // ---- Neutrals & the two inks ----
    public const string Paper = "#FAF8F2";
    public const string PaperBright = "#FFFFFF";
    public const string PaperDeep = "#F1EBDD";
    public const string PaperShade = "#E9E1CF";
    public const string Hairline = "#E3DCC9";
    public const string Ink = "#33322B";
    public const string InkSoft = "#6B6759";
    public const string InkMuted = "#8A8578";
    public const string InkBlue = "#2C3E5E"; // "the pen": signatures, editorial marks, the owner's voice

    // The app accent is the Action role (pine); terracotta is retired. Accent is the
    // mark tier (buttons, marker underlines, active/selected); AccentDeep is the deep
    // tier (pressed fill / accent text at any size). PaperBright text on Accent clears AA.
    public const string Accent = ActionMark;
    public const string AccentDeep = ActionDeep;

    // ---- P4 role model (App profile) — re-exported from PaperColorTokens ----
    // Each role carries fill (T2) · mark (T3) · text (T4) · deep (T5).
    public const string ActionFill = "#A5C8AD"; public const string ActionMark = "#38734A"; public const string ActionText = "#295E3A"; public const string ActionDeep = "#174025";
    public const string PositiveFill = "#B1C69A"; public const string PositiveMark = "#517025"; public const string PositiveText = "#405B18"; public const string PositiveDeep = "#293E08";
    public const string ResolvedFill = "#C0BDB9"; public const string ResolvedMark = "#68645E"; public const string ResolvedText = "#54514B"; public const string ResolvedDeep = "#393632";
    public const string ReferenceFill = "#AEC0D5"; public const string ReferenceMark = "#4C6787"; public const string ReferenceText = "#3B536F"; public const string ReferenceDeep = "#26384E";
    public const string CautionFill = "#D0BB91"; public const string CautionMark = "#805F00"; public const string CautionText = "#694C00"; public const string CautionDeep = "#493200";
    public const string ProblemFill = "#E8ACAD"; public const string ProblemMark = "#9E434B"; public const string ProblemText = "#82333B"; public const string ProblemDeep = "#5C1F26";
    public const string ProgressFill = "#9BC7CD"; public const string ProgressMark = "#18717C"; public const string ProgressText = "#085C65"; public const string ProgressDeep = "#003F46";

    /// <summary>
    /// Per-app signature hues (DESIGN.md §3 identity set), kept separate from the
    /// semantic roles so a tab or accent never reads as "caution". Each app's hue is
    /// sampled from its painted cover during its own phase, which is why these are
    /// nested by app rather than pooled: a second app getting an identity is exactly
    /// what makes a bare "Identity.Mark" ambiguous.
    /// </summary>
    public static class Identity
    {
        /// <summary>
        /// Flyleaf — warm study amber. PROVISIONAL: three tiers rather than the role
        /// model's five, pending a resample from the painted cover.
        /// </summary>
        public static class Flyleaf
        {
            public const string Warm = "#C9803A";
            public const string Mark = "#B4552F";
            public const string Deep = "#8A4A22";
        }

        /// <summary>
        /// Score — teal-slate, sampled from the ribbon cover and laddered into the
        /// role model's five tiers.
        ///
        /// The hue was nudged from the as-painted 174 degrees to 165, by rotating the
        /// painting rather than re-prompting it, so the paper texture and pigment
        /// blooms are exactly as painted and only the hue moved. That shift is
        /// load-bearing: at 174 it sat 13 degrees from the in-progress status teal,
        /// which is too close for a set whose whole job is to not read as a status.
        /// At 165 the separation is 21 degrees.
        /// </summary>
        public static class Score
        {
            public const string Fill = "#AFC0BC";
            public const string Ground = "#93ABA5";
            public const string Mark = "#436A61";
            public const string Text = "#35544D";
            public const string Deep = "#263B36";
        }
    }

    /// <summary>
    /// Realistic-shelf material tones for the Flyleaf home + tray only — the scoped
    /// DESIGN.md §1/§5 exception. These are imagery (a Skia-rendered bookcase), not tokened
    /// UI text; they live here so no raw literal leaks the guard. PROVISIONAL pending the
    /// on-device render.
    /// </summary>
    public static class Shelf
    {
        public const string Walnut = "#6B4A2E";
        public const string WalnutDeep = "#3A2312";
        public const string WalnutLight = "#9A6437";
        public const string Firelight = "#EFB877";
        public const string Bronze = "#7C6A3E";
        public const string BronzeDeep = "#4A3E22";
    }

    /// <summary>
    /// Type scale in Android scaled pixels (sp), per DESIGN.md §4's reworked scale —
    /// bigger, with more contrast at the top. Display is Serif <b>Black</b> and is the
    /// largest thing on a screen; Countdown and CoverTitle sit outside the reading
    /// ladder as single-purpose sizes.
    /// </summary>
    public static class TypeSp
    {
        public const float CoverTitle = 72f;
        public const float Display = 44f;
        public const float Countdown = 40f;
        public const float Title = 28f;
        public const float Voice = 26f;
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

    /// <summary>
    /// The translucency the palette carries instead of committing to a hex
    /// (DESIGN.md §3): the wash tier is a role's Mark hue drawn at this alpha, and
    /// it is what §8's Selected state paints behind full-strength ink.
    ///
    /// Byte-typed on purpose. Every consumer builds a colour from it, and Android's
    /// Color has both a (byte,byte,byte,byte) and an (int,int,int,int) constructor
    /// — an int here makes the call ambiguous at every site.
    /// </summary>
    public static class Alpha
    {
        public const byte Wash = 77;   // ~30%
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

    /// <summary>
    /// Font asset paths as packaged by Codex.Ui.Android, whose csproj globs
    /// <c>Fonts/*.ttf</c> — so every face here already ships in every APK.
    /// <b>Black</b> anchors the reworked Display size and <b>Script</b> (Pinyon) is
    /// the calligraphy hand DESIGN.md §4 reserves for the owner's voice; both were
    /// being packaged and never declared, so neither could be used.
    /// </summary>
    public static class FontAssets
    {
        public const string SerifRegular = "fonts/SourceSerif4-Regular.ttf";
        public const string SerifSemibold = "fonts/SourceSerif4-Semibold.ttf";
        public const string SerifBlack = "fonts/SourceSerif4-Black.ttf";
        public const string SerifItalic = "fonts/SourceSerif4-Italic.ttf";
        public const string SansRegular = "fonts/SourceSans3-Regular.ttf";
        public const string SansSemibold = "fonts/SourceSans3-Semibold.ttf";
        public const string Mono = "fonts/CascadiaCode-Regular.ttf";
        public const string Script = "fonts/PinyonScript-Regular.ttf";
    }
}
