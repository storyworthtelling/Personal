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
    private static readonly string[] Cloths =
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

    // An app that has earned a signature identity hue (sampled from its own painted cover during its
    // rebrand phase) binds in that hue, so its cloth agrees with its cover instead of a hashed tone
    // fighting it. Apps without a shipped identity fall back to the stable hash below. This is the
    // "identity hue if the app has one, hashed fallback if not" model (owner decision). Entries are
    // added as each app's identity lands; contrast across the six home books is a separate concern
    // (exactly one app should hold a pale cloth — kept a hashed/curated call, not forced here).
    private static readonly IReadOnlyDictionary<string, string> IdentityCloths = new Dictionary<string, string>();

    /// <summary>The cloth tone (a DesignTokens hex string) for a package — the app's identity hue if it
    /// has one, otherwise a stable hash of the id.</summary>
    public static string ForPackage(string package) =>
        IdentityCloths.TryGetValue(package, out var identity) ? identity : For(package);

    /// <summary>
    /// The cloth tone for any stable key. Nothing about the binding is app-specific —
    /// it is a deterministic pick from the same ten tones — so the crate's stepped
    /// sleeve edges use it too, and a record keeps its cloth for as long as it is in
    /// the run.
    /// </summary>
    /// <remarks>
    /// Deliberately does <i>not</i> consult <see cref="IdentityCloths"/>: an identity hue
    /// belongs to an app, and a key that is not a package id has no identity to honor.
    /// </remarks>
    public static string For(string key) => Cloths[StableIndex(key)];

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
