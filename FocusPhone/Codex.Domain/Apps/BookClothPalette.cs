using Codex.Domain.Design;

namespace Codex.Domain.Apps;

/// <summary>
/// Assigns each app a stable "book cloth" tone for its spine / fallback cover, so the shelf
/// reads as a set of individually-bound books rather than a uniform block. The tones are drawn
/// only from registered design tokens (no new color literals — the leak-guard stays green), and
/// the pick is a deterministic hash of the package id, so an app keeps the same binding across
/// launches and the mapping is unit-testable. Apps with a real painted cover ignore this.
/// </summary>
public static class BookClothPalette
{
    // Deep, muted tones that read as cloth/leather bindings — role "deep" tiers, the identity
    // hues, the pen blue, and the walnut tones. All are DesignTokens references (no literals here).
    /// <summary>The tones an app with no chosen binding is hashed onto.</summary>
    public static readonly string[] Cloths =
    [
        DesignTokens.Identity.Flyleaf.Warm,
        DesignTokens.Identity.Flyleaf.Mark,
        DesignTokens.Identity.Flyleaf.Deep,
        DesignTokens.ActionDeep,
        DesignTokens.ReferenceDeep,
        DesignTokens.CautionDeep,
        DesignTokens.ProblemDeep,
        DesignTokens.ProgressDeep,
        DesignTokens.InkBlue,
        DesignTokens.Shelf.WalnutLight,
    ];

    // The binding each app is bound in, once someone has chosen one.
    //
    // DESIGN.md §12 is explicit that a binding is NOT required to match the app's
    // identity hue: "a binding that harmonises with the cover art makes the book the
    // hardest of six to pick out of the row." So this is a chosen cloth per app, not
    // a lookup into the identity ladder — Score's is cream linen precisely because it
    // is the only light book on the shelf and so is findable at a glance.
    //
    // Anything absent falls through to the stable hash, which is what keeps a shelf
    // of unstyled third-party apps looking like a set of bound books.
    private static readonly IReadOnlyDictionary<string, string> Bindings =
        new Dictionary<string, string>
        {
            [HomeFeatureSet.AudioPackage] = DesignTokens.PaperShade,
        };

    /// <summary>The cloth tone (a DesignTokens hex string) for a package — the binding it was
    /// given if it has one, otherwise a stable hash of the id.</summary>
    public static string ForPackage(string package) =>
        Bindings.TryGetValue(package, out var chosen) ? chosen : For(package);

    /// <summary>
    /// The cloth tone for any stable key. Nothing about the binding is app-specific —
    /// it is a deterministic pick from the same ten tones — so the crate's stepped
    /// sleeve edges use it too, and a record keeps its cloth for as long as it is in
    /// the run.
    /// </summary>
    /// <remarks>
    /// Deliberately does <i>not</i> consult <see cref="Bindings"/>: a binding is chosen
    /// for an app, and a key that is not a package id has no binding to honor.
    /// </remarks>
    public static string For(string key) => Cloths[StableIndex(key)];

    /// <summary>
    /// Whether the plate's marks — the fine rule, the corner flourishes, the window
    /// keyline and the title — have to invert to ink on this cloth.
    ///
    /// They are paper-coloured by default, which works on every dark binding. On a
    /// light one it produces cream lettering on cream linen, which is not a subtle
    /// contrast problem: the title disappears. DESIGN.md §12 calls for the inversion
    /// by name for Score, and deriving it from the cloth rather than naming the app
    /// means the next light binding cannot get it wrong.
    /// </summary>
    public static bool MarksInvertToInk(string clothHex) => Luminance(clothHex) > 0.6;

    // Rec. 601 luma over the sRGB bytes. Good enough for "is this cloth light",
    // which is the only question being asked, and it needs no colour-space types in
    // a Domain that deliberately has none.
    private static double Luminance(string hex)
    {
        var value = hex.TrimStart('#');
        var r = Convert.ToInt32(value[..2], 16);
        var g = Convert.ToInt32(value.Substring(2, 2), 16);
        var b = Convert.ToInt32(value.Substring(4, 2), 16);
        return ((0.299 * r) + (0.587 * g) + (0.114 * b)) / 255.0;
    }

    // FNV-1a over the ordinal bytes — deterministic across processes (unlike string.GetHashCode),
    // so the binding is stable and the tests are not flaky.
    private static int StableIndex(string key)
    {
        uint hash = 2166136261u;
        foreach (var c in key)
        {
            hash = (hash ^ c) * 16777619u;
        }
        return (int)(hash % (uint)Cloths.Length);
    }
}
