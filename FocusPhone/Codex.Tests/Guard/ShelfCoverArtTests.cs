using Codex.Domain.Policy;
using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Every suite app the shelf can draw cover-out has its own window illustration, or is on a short
/// list saying it has none yet.
///
/// <para><c>FrozenInvariantsTests.EveryFeaturedHomeBookHasItsOwnCoverIllustration</c> already checks
/// this, but only for the <b>six default featured</b> packages. The tray draws the owner's other
/// repo apps cover-out too, and those went unchecked — so <b>Foreword shipped with a circle</b>: no
/// <c>CoverResources</c> entry, the silent fallback fills the window with cloth and centres the
/// launcher icon, and a launcher icon is adaptive, which Android masks into a circle. A square
/// window with a small round picture floating in it, on a shelf where every other book is a
/// painting. Nothing failed; the art was sitting in <c>Branding/</c> the whole time.</para>
///
/// <para>So the rule is stated over the whole suite rather than over the featured six, and an app
/// with no art is named here rather than being indistinguishable from one that was forgotten.
/// Giving a book its cover means deleting a line from <see cref="NoArtYet"/>, which is a
/// decision somebody makes on purpose.</para>
/// </summary>
public class ShelfCoverArtTests
{
    /// <summary>
    /// Every <c>com.storyworthtelling.*</c> app that ships an APK, and so can appear on the shelf.
    /// Flyleaf hosts the shelf and is never a book on it.
    /// </summary>
    private static readonly (string Package, string Name)[] SuiteBooks =
    [
        (PolicyContext.CommunicationPackage, "Post"),
        (PolicyContext.PlatePackage, "Plate"),
        (PolicyContext.AudioPackage, "Score"),
        (PolicyContext.ForewordPackage, "Foreword"),
        (PolicyContext.IndexPackage, "Index"),
        (PolicyContext.FolioPackage, "Folio"),
        (PolicyContext.PulpPackage, "Pulp"),
    ];

    /// <summary>
    /// The apps with no painting yet. They fall back to the launcher icon on purpose, and that is
    /// the one case where the fallback is honest. Empty since P12: this list still named Folio as
    /// "Canonizer, no art yet" long after IconStudio began writing <c>cover_folio.png</c> into
    /// Flyleaf, so Folio's book showed its launcher icon with a painting sitting unused beside it.
    /// The Ledger app is retired (its package id is Index's now), so it is not a book at all.
    /// </summary>
    private static readonly string[] NoArtYet = [];

    /// <summary>
    /// The shelf's Index package is the id Index ships under — the same one Post's balance
    /// handoff names — and not the retired Ledger app's, whose package id Index took over.
    /// </summary>
    [Fact]
    public void TheShelfNamesIndexByItsShippingPackageNotTheRetiredLedgerOne()
    {
        Assert.Equal("com.storyworthtelling.index", PolicyContext.IndexPackage);
        Assert.Equal(IndexHandoff.Package, PolicyContext.IndexPackage);
        Assert.StartsWith(PolicyContext.OwnerPackagePrefix, PolicyContext.IndexPackage, StringComparison.Ordinal);
        Assert.DoesNotContain(SuiteBooks, book => book.Package.EndsWith(".ledger", StringComparison.Ordinal));
    }

    [Fact]
    public void EverySuiteBookOnTheShelfHasAWindowIllustration()
    {
        var block = CoverResourcesBlock();

        var missing = SuiteBooks
            .Where(book => !NoArtYet.Contains(book.Package))
            .Where(book => !block.Contains(book.Package, StringComparison.Ordinal)
                           && !MappedByConstant(block, book.Package))
            .Select(book => $"{book.Name} ({book.Package})")
            .ToList();

        Assert.True(missing.Count == 0,
            "These are drawn cover-out on the shelf with no window illustration, so they fall back "
            + "to a circular launcher icon on plain cloth: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The drawable a mapped package names actually exists. A resource id that does not resolve is
    /// a build error, but a mapping pointing at the WRONG app's art is not — and on a shelf of
    /// paintings that is the kind of thing nobody notices for a month.
    /// </summary>
    [Fact]
    public void EveryMappedCoverHasItsDrawable()
    {
        var block = CoverResourcesBlock();

        var missing = SuiteBooks
            .Where(book => !NoArtYet.Contains(book.Package))
            .Select(book => book.Name.ToLowerInvariant())
            .Where(name => block.Contains($"cover_{name}", StringComparison.Ordinal))
            .Where(name => !File.Exists(Path.Combine(
                Design.DesignTestHelpers.FocusPhoneRoot(),
                "Flyleaf.Android", "Resources", "drawable", $"cover_{name}.png")))
            .ToList();

        Assert.True(missing.Count == 0,
            "Mapped to a drawable that is not in Flyleaf's drawable folder: " + string.Join(", ", missing));
    }

    private static string CoverResourcesBlock()
    {
        var home = Path.Combine(
            Design.DesignTestHelpers.FocusPhoneRoot(), "Flyleaf.Android", "HomeActivity.cs");
        Assert.True(File.Exists(home), "Flyleaf's HomeActivity has moved; this guard watches nothing.");

        var source = File.ReadAllText(home);
        var start = source.IndexOf("CoverResources = new()", StringComparison.Ordinal);
        Assert.True(start >= 0, "HomeActivity no longer declares a CoverResources map.");

        return source[start..source.IndexOf("};", start, StringComparison.Ordinal)];
    }

    private static bool MappedByConstant(string block, string package)
    {
        // The map is keyed by constants rather than package literals, and the constant Flyleaf
        // names is not always the one Domain calls it — the camera head is PlatePackage here and
        // CameraPackage in HomeFeatureSet. Match on the suffix both spellings share.
        var constantSuffix = package switch
        {
            _ when package == PolicyContext.CommunicationPackage => "CommunicationPackage",
            _ when package == PolicyContext.PlatePackage => "CameraPackage",
            _ when package == PolicyContext.AudioPackage => "AudioPackage",
            _ when package == PolicyContext.ForewordPackage => "ForewordPackage",
            _ when package == PolicyContext.IndexPackage => "IndexPackage",
            _ when package == PolicyContext.FolioPackage => "FolioPackage",
            _ when package == PolicyContext.PulpPackage => "PulpPackage",
            _ => null,
        };

        return constantSuffix is not null && block.Contains(constantSuffix, StringComparison.Ordinal);
    }
}
