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
    public static readonly Color Accent = Color.ParseColor(DesignTokens.Accent);
    public static readonly Color AccentDeep = Color.ParseColor(DesignTokens.AccentDeep);

    // Flyleaf's realistic bookshelf surface (a scoped DESIGN.md §1/§5 exception): warm identity
    // and the walnut/firelight/bronze material tones, parsed once from the tokens.
    public static readonly Color IdentityWarm = Color.ParseColor(DesignTokens.Identity.Warm);
    public static readonly Color IdentityMark = Color.ParseColor(DesignTokens.Identity.Mark);
    public static readonly Color IdentityDeep = Color.ParseColor(DesignTokens.Identity.Deep);
    public static readonly Color Walnut = Color.ParseColor(DesignTokens.Shelf.Walnut);
    public static readonly Color WalnutDeep = Color.ParseColor(DesignTokens.Shelf.WalnutDeep);
    public static readonly Color WalnutLight = Color.ParseColor(DesignTokens.Shelf.WalnutLight);
    public static readonly Color Firelight = Color.ParseColor(DesignTokens.Shelf.Firelight);
    public static readonly Color Bronze = Color.ParseColor(DesignTokens.Shelf.Bronze);
    public static readonly Color BronzeDeep = Color.ParseColor(DesignTokens.Shelf.BronzeDeep);

    private static Typeface? serifRegular;
    private static Typeface? serifSemibold;
    private static Typeface? serifItalic;
    private static Typeface? sansRegular;
    private static Typeface? sansSemibold;
    private static Typeface? mono;

    public static Typeface SerifRegular(Context context) =>
        serifRegular ??= Load(context, DesignTokens.FontAssets.SerifRegular);

    public static Typeface SerifSemibold(Context context) =>
        serifSemibold ??= Load(context, DesignTokens.FontAssets.SerifSemibold);

    public static Typeface SerifItalic(Context context) =>
        serifItalic ??= Load(context, DesignTokens.FontAssets.SerifItalic);

    public static Typeface SansRegular(Context context) =>
        sansRegular ??= Load(context, DesignTokens.FontAssets.SansRegular);

    public static Typeface SansSemibold(Context context) =>
        sansSemibold ??= Load(context, DesignTokens.FontAssets.SansSemibold);

    public static Typeface Mono(Context context) =>
        mono ??= Load(context, DesignTokens.FontAssets.Mono);

    private static Typeface Load(Context context, string assetPath) =>
        Typeface.CreateFromAsset(context.Assets, assetPath)
            ?? throw new InvalidOperationException($"Font asset not packaged: {assetPath}");

    /// <summary>Converts a DesignTokens.Space/Shape dp value to device pixels.</summary>
    public static int Dp(Context context, int dp) =>
        (int)(dp * context.Resources!.DisplayMetrics!.Density + 0.5f);
}
