using Android.Content;
using Android.Graphics;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>
/// Runtime access to the Paper &amp; Ink tokens for imperatively built UI:
/// parsed colors, cached typefaces (loaded once from the shared font assets),
/// and dp→px conversion so all spacing comes from the DesignTokens scale.
/// </summary>
public static class PaperTheme
{
    public static readonly Color Paper = Color.ParseColor(DesignTokens.Paper);
    public static readonly Color PaperBright = Color.ParseColor(DesignTokens.PaperBright);
    public static readonly Color PaperDeep = Color.ParseColor(DesignTokens.PaperDeep);
    public static readonly Color PaperShade = Color.ParseColor(DesignTokens.PaperShade);
    public static readonly Color Hairline = Color.ParseColor(DesignTokens.Hairline);
    public static readonly Color Ink = Color.ParseColor(DesignTokens.Ink);
    public static readonly Color InkSoft = Color.ParseColor(DesignTokens.InkSoft);
    public static readonly Color InkMuted = Color.ParseColor(DesignTokens.InkMuted);
    /// <summary>"The pen" — signatures, editorial marks, and the colour of the owner's voice.</summary>
    public static readonly Color InkBlue = Color.ParseColor(DesignTokens.InkBlue);
    public static readonly Color Accent = Color.ParseColor(DesignTokens.Accent);
    public static readonly Color AccentDeep = Color.ParseColor(DesignTokens.AccentDeep);

    // The role tiers the components reach for by name (DESIGN.md §3): Action for a
    // lead intent, Problem for a destructive one, Caution for a soft heads-up, and
    // In-progress for "something is running". Parsed once, like everything above.
    public static readonly Color ActionText = Color.ParseColor(DesignTokens.ActionText);
    public static readonly Color ProblemMark = Color.ParseColor(DesignTokens.ProblemMark);
    public static readonly Color CautionMark = Color.ParseColor(DesignTokens.CautionMark);
    public static readonly Color ProgressFill = Color.ParseColor(DesignTokens.ProgressFill);
    public static readonly Color ProgressMark = Color.ParseColor(DesignTokens.ProgressMark);
    public static readonly Color ProgressText = Color.ParseColor(DesignTokens.ProgressText);

    // Flyleaf's realistic bookshelf surface (a scoped DESIGN.md §1/§5 exception): warm identity
    // and the walnut/firelight/bronze material tones, parsed once from the tokens.
    // Per-app identity hues (DESIGN.md §3). Named for their app, because the kit is
    // shared and a bare "Identity" stopped meaning anything the moment a second app
    // had one.
    public static readonly Color FlyleafWarm = Color.ParseColor(DesignTokens.Identity.Flyleaf.Warm);
    public static readonly Color FlyleafMark = Color.ParseColor(DesignTokens.Identity.Flyleaf.Mark);
    public static readonly Color FlyleafDeep = Color.ParseColor(DesignTokens.Identity.Flyleaf.Deep);

    public static readonly Color ScoreFill = Color.ParseColor(DesignTokens.Identity.Score.Fill);
    public static readonly Color ScoreGround = Color.ParseColor(DesignTokens.Identity.Score.Ground);
    public static readonly Color ScoreMark = Color.ParseColor(DesignTokens.Identity.Score.Mark);
    public static readonly Color ScoreText = Color.ParseColor(DesignTokens.Identity.Score.Text);
    public static readonly Color ScoreDeep = Color.ParseColor(DesignTokens.Identity.Score.Deep);
    public static readonly Color PostFill = Color.ParseColor(DesignTokens.Identity.Post.Fill);
    public static readonly Color PostGround = Color.ParseColor(DesignTokens.Identity.Post.Ground);
    public static readonly Color PostMark = Color.ParseColor(DesignTokens.Identity.Post.Mark);
    public static readonly Color PostText = Color.ParseColor(DesignTokens.Identity.Post.Text);
    public static readonly Color PostDeep = Color.ParseColor(DesignTokens.Identity.Post.Deep);
    public static readonly Color PlateFill = Color.ParseColor(DesignTokens.Identity.Plate.Fill);
    public static readonly Color PlateGround = Color.ParseColor(DesignTokens.Identity.Plate.Ground);
    public static readonly Color PlateMark = Color.ParseColor(DesignTokens.Identity.Plate.Mark);
    public static readonly Color PlateText = Color.ParseColor(DesignTokens.Identity.Plate.Text);
    public static readonly Color PlateDeep = Color.ParseColor(DesignTokens.Identity.Plate.Deep);

    // The fill tiers exist here because §9 cuts an inactive binder tab from its area's
    // own cloth, which is exactly that tier. Resolved is the neutral every app's
    // Settings tab takes, so the one area they all share looks the same everywhere.
    // Reference is Plate's Scan tab: a scan is a document being referred to, not an act in
    // progress, and §9 lets a middle area draw on a semantic role rather than invent a hue.
    public static readonly Color ReferenceFill = Color.ParseColor(DesignTokens.ReferenceFill);
    public static readonly Color ReferenceMark = Color.ParseColor(DesignTokens.ReferenceMark);
    public static readonly Color ResolvedFill = Color.ParseColor(DesignTokens.ResolvedFill);
    public static readonly Color ResolvedMark = Color.ParseColor(DesignTokens.ResolvedMark);
    public static readonly Color CautionFill = Color.ParseColor(DesignTokens.CautionFill);
    public static readonly Color PositiveFill = Color.ParseColor(DesignTokens.PositiveFill);
    public static readonly Color PositiveMark = Color.ParseColor(DesignTokens.PositiveMark);

    public static readonly Color Walnut = Color.ParseColor(DesignTokens.Shelf.Walnut);
    public static readonly Color WalnutDeep = Color.ParseColor(DesignTokens.Shelf.WalnutDeep);
    public static readonly Color WalnutLight = Color.ParseColor(DesignTokens.Shelf.WalnutLight);
    public static readonly Color Firelight = Color.ParseColor(DesignTokens.Shelf.Firelight);
    public static readonly Color Bronze = Color.ParseColor(DesignTokens.Shelf.Bronze);
    public static readonly Color BronzeDeep = Color.ParseColor(DesignTokens.Shelf.BronzeDeep);

    private static Typeface? serifRegular;
    private static Typeface? serifSemibold;
    private static Typeface? serifBlack;
    private static Typeface? serifItalic;
    private static Typeface? sansRegular;
    private static Typeface? sansSemibold;
    private static Typeface? mono;
    private static Typeface? script;

    public static Typeface SerifRegular(Context context) =>
        serifRegular ??= Load(context, DesignTokens.FontAssets.SerifRegular);

    public static Typeface SerifSemibold(Context context) =>
        serifSemibold ??= Load(context, DesignTokens.FontAssets.SerifSemibold);

    /// <summary>The weight the reworked Display size is set in (DESIGN.md §4).</summary>
    public static Typeface SerifBlack(Context context) =>
        serifBlack ??= Load(context, DesignTokens.FontAssets.SerifBlack);

    public static Typeface SerifItalic(Context context) =>
        serifItalic ??= Load(context, DesignTokens.FontAssets.SerifItalic);

    public static Typeface SansRegular(Context context) =>
        sansRegular ??= Load(context, DesignTokens.FontAssets.SansRegular);

    public static Typeface SansSemibold(Context context) =>
        sansSemibold ??= Load(context, DesignTokens.FontAssets.SansSemibold);

    public static Typeface Mono(Context context) =>
        mono ??= Load(context, DesignTokens.FontAssets.Mono);

    /// <summary>Pinyon Script — reserved for the owner's voice, always in ink-blue.</summary>
    public static Typeface Script(Context context) =>
        script ??= Load(context, DesignTokens.FontAssets.Script);

    private static Typeface Load(Context context, string assetPath) =>
        Typeface.CreateFromAsset(context.Assets, assetPath)
            ?? throw new InvalidOperationException($"Font asset not packaged: {assetPath}");

    /// <summary>
    /// A hue at the wash tier (DESIGN.md §3): the same colour, carried translucent.
    /// This is the band §8's Selected state sits behind — one implementation, so a
    /// selected row, a chosen picker option and a lit segment are the same weight.
    /// </summary>
    public static Color Wash(Color hue) =>
        new(hue.R, hue.G, hue.B, DesignTokens.Alpha.Wash);

    // The tiled ink-fleck bitmap every paper ground uses (paper_grain.png, generated
    // by Tools/IconStudio — DESIGN.md §2/§6), decoded once and reused for every caller.
    private static Bitmap? grainBitmap;

    /// <summary>
    /// A copy of <paramref name="basePaint"/> with the shared paper-grain tile as its
    /// Shader, for drawing a second, translucent ink-speckle pass directly on top of a
    /// shape another Paint just drew — DESIGN.md §2 gives Marker and Colored pencil
    /// fills the same "always on" grain the paper ground carries, and a Shader-based
    /// overlay picks up whatever the base Paint's own style already describes (a
    /// stroke, an arc, a plain rect) rather than needing a second drawable layered on
    /// and separately clipped to match. Alpha resets to opaque so the grain reads at
    /// its own baked-in intensity regardless of the base paint's.
    /// </summary>
    public static Paint GrainOverlay(Context context, Paint basePaint)
    {
        grainBitmap ??= BitmapFactory.DecodeResource(context.Resources, Resource.Drawable.paper_grain)!;
        var overlay = new Paint(basePaint) { Alpha = 255 };
        overlay.SetShader(new BitmapShader(grainBitmap, Shader.TileMode.Repeat!, Shader.TileMode.Repeat!));
        return overlay;
    }

    /// <summary>Converts a DesignTokens.Space/Shape dp value to device pixels.</summary>
    public static int Dp(Context context, int dp) =>
        (int)(dp * context.Resources!.DisplayMetrics!.Density + 0.5f);

    /// <summary>
    /// A type size in device pixels, for the few places that set text on a Paint
    /// rather than a TextView — a canvas has no notion of sp, but the reader's font
    /// scale still applies to what they are reading.
    /// </summary>
    public static float Sp(Context context, float sp) =>
        sp * context.Resources!.DisplayMetrics!.ScaledDensity;
}
