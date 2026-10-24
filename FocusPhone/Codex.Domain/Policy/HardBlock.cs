namespace Codex.Domain.Policy;

/// <summary>
/// The always-hidden app categories the owner never wants on the phone — Games, Social apps (any
/// app declaring the social category, except Google's own and the owner's suite), Dating,
/// Gambling/betting, and Short-form video. Unlike the reversible Admin blocklist, these
/// are hard-suspended by policy and never surfaced, not even in Admin. Detected by BOTH Android's
/// declared application category AND a curated package list, because many apps mis-declare their
/// category. (Browsers other than the Flyleaf web view are handled separately, by the existing
/// browser-suspension path.) Pure so the rule is unit-tested without a device; the head applies
/// <see cref="Detect"/> to the installed apps and hands the result to the policy plan.
/// </summary>
public static class HardBlock
{
    // Android's ApplicationInfo.Category ints. Only Game maps cleanly to a never-visible category.
    private const int CategoryGame = 0;
    private const int CategorySocial = 4;

    // Google's own namespace (Messages, Contacts, Meet, Gemini, Lens, YouTube …) and the owner's suite.
    // Many of these declare the social category, so the social rule must not catch them.
    private const string GooglePackagePrefix = "com.google.";

    public static bool IsNeverVisibleCategory(int androidCategory) =>
        androidCategory is CategoryGame;

    public static bool IsSocialCategory(int androidCategory) =>
        androidCategory is CategorySocial;

    /// <summary>True for Google's own apps and the owner's suite: never blocked by the social rule.</summary>
    public static bool IsFirstParty(string package) =>
        package.StartsWith(GooglePackagePrefix, StringComparison.Ordinal)
        || package.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal);

    /// <summary>Well-known packages in the never-visible categories that don't reliably self-declare.
    /// Not exhaustive — extend as new ones surface; the Android-category check catches the rest.</summary>
    public static IReadOnlySet<string> CuratedPackages { get; } = new HashSet<string>
    {
        // Social media
        "com.facebook.katana", "com.facebook.lite", "com.instagram.android", "com.twitter.android",
        "com.snapchat.android", "com.reddit.frontpage", "com.pinterest", "com.linkedin.android",
        "com.facebook.orca", "com.discord", "com.tumblr", "com.zhiliaoapp.musically.go",
        // Short-form video
        "com.zhiliaoapp.musically", "com.ss.android.ugc.trill", "com.lemon.lvoverseas",
        // Dating
        "com.tinder", "com.bumble.app", "co.hinge.app", "com.match.android.matchmobile",
        "com.okcupid.okcupid", "com.pof.android", "com.grindrapp.android", "com.coffeemeetsbagel",
        // Gambling / betting
        "com.draftkings.sportsbook", "com.fanduel.sportsbook", "com.betmgm.sportsbook",
        "com.caesars.sportsbook.android", "com.williamhill.us.nj", "com.betrivers",
        // A few large games that don't declare the Game category reliably
        "com.king.candycrushsaga", "com.supercell.clashofclans", "com.roblox.client",
        "com.mojang.minecraftpe", "com.dts.freefireth",
    };

    /// <summary>The installed packages that fall in a never-visible category, by declared category
    /// OR the curated list.</summary>
    public static IReadOnlySet<string> Detect(IEnumerable<(string Package, int AndroidCategory)> installed)
    {
        var blocked = new HashSet<string>();
        foreach (var (package, androidCategory) in installed)
        {
            var social = IsSocialCategory(androidCategory) && !IsFirstParty(package);
            if (IsNeverVisibleCategory(androidCategory) || social || CuratedPackages.Contains(package))
            {
                blocked.Add(package);
            }
        }
        return blocked;
    }
}
