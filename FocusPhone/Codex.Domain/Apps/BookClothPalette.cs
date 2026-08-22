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
        DesignTokens.Identity.Warm,
        DesignTokens.Identity.Mark,
        DesignTokens.Identity.Deep,
        DesignTokens.ActionDeep,
        DesignTokens.ReferenceDeep,
        DesignTokens.CautionDeep,
        DesignTokens.ProblemDeep,
        DesignTokens.ProgressDeep,
        DesignTokens.InkBlue,
        DesignTokens.Shelf.WalnutLight,
    ];

    /// <summary>The cloth tone (a DesignTokens hex string) for a package — stable per id.</summary>
    public static string ForPackage(string package) => Cloths[StableIndex(package)];

    // FNV-1a over the ordinal bytes — deterministic across processes (unlike string.GetHashCode),
    // so the binding is stable and the tests are not flaky.
    private static int StableIndex(string package)
    {
        uint hash = 2166136261u;
        foreach (var c in package)
        {
            hash = (hash ^ c) * 16777619u;
        }
        return (int)(hash % (uint)Cloths.Length);
    }
}
