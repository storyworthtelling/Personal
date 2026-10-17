using Codex.Domain.Apps;
using Codex.Domain.Design;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Apps;

public class BookClothPaletteTests
{
    [Fact]
    public void ForPackage_IsStable_AcrossCalls()
    {
        Assert.Equal(
            BookClothPalette.ForPackage("com.example.unbound"),
            BookClothPalette.ForPackage("com.example.unbound"));
    }

    [Fact]
    public void ForPackage_AlwaysReturnsARegisteredToken_NoRawLiterals()
    {
        var tokens = new HashSet<string>
        {
            DesignTokens.Identity.Flyleaf.Warm, DesignTokens.Identity.Flyleaf.Mark, DesignTokens.Identity.Flyleaf.Deep,
            DesignTokens.ActionDeep, DesignTokens.ReferenceDeep, DesignTokens.CautionDeep,
            DesignTokens.ProblemDeep, DesignTokens.ProgressDeep, DesignTokens.InkBlue,
            DesignTokens.Shelf.WalnutLight,
        };

        foreach (var package in new[] { "com.a", "com.b", "com.spotify.music", "com.google.android.apps.maps", "x" })
        {
            Assert.Contains(BookClothPalette.ForPackage(package), tokens);
        }
    }

    [Fact]
    public void DifferentPackages_SpreadAcrossThePalette()
    {
        // Not a uniform block: a handful of ids should touch more than one cloth.
        var picks = Enumerable.Range(0, 20)
            .Select(i => BookClothPalette.ForPackage($"com.app.number{i}"))
            .Distinct()
            .Count();

        Assert.True(picks > 1, "cloth tones should vary across packages");
    }

    // The crate's stepped sleeve edges bind on a pick id rather than a package. It is
    // the same deterministic pick from the same tones, so a record keeps its cloth
    // for as long as it is in the run.
    [Fact]
    public void For_AgreesWithForPackage()
    {
        // An app with no chosen binding; a bound suite app deliberately disagrees with the hash.
        Assert.Equal(
            BookClothPalette.ForPackage("com.example.unbound"),
            BookClothPalette.For("com.example.unbound"));
    }

    // DESIGN.md §12: "Score's binding is cream linen with the rule, flourishes and
    // lettering inverted to ink — chosen because it is the only light book on the
    // shelf and so is findable at a glance." The shelf shipped it in hashed walnut,
    // because the per-app binding map existed but was empty.
    [Fact]
    public void ForPackage_BindsScoreInCreamLinenRatherThanAHashedTone()
    {
        Assert.Equal(DesignTokens.PaperShade, BookClothPalette.ForPackage(HomeFeatureSet.AudioPackage));
    }

    [Fact]
    public void ForPackage_StillHashesAnAppWithNoChosenBinding()
    {
        var unbound = BookClothPalette.ForPackage("com.example.nothing");

        Assert.Contains(unbound, BookClothPalette.Cloths);
    }

    // The plate's rule, corner flourishes, keyline and title are drawn in one ink.
    // On a dark cloth that ink is paper; on a light one it has to invert, or a cream
    // book carries cream lettering on cream linen and cannot be read at all.
    [Fact]
    public void MarksInvertToInk_OnALightBinding()
    {
        Assert.True(BookClothPalette.MarksInvertToInk(DesignTokens.PaperShade));
        Assert.True(BookClothPalette.MarksInvertToInk(DesignTokens.Paper));
        Assert.True(BookClothPalette.MarksInvertToInk(DesignTokens.PaperBright));
    }

    [Fact]
    public void MarksStayPaper_OnEveryDarkClothTheShelfCanPick()
    {
        foreach (var cloth in BookClothPalette.Cloths.Where(c => c != DesignTokens.PaperShade))
        {
            Assert.False(BookClothPalette.MarksInvertToInk(cloth), cloth);
        }
    }

    /// <summary>
    /// The suite's own books are bound deliberately, never by the hash.
    ///
    /// The hash exists so a shelf of third-party apps reads as a set of bound books; it is
    /// not a design decision, and for our own heads it actively fights one. Plate's book was
    /// bound in Shelf.WalnutLight — a bookcase MATERIAL tone, so the book was the same colour
    /// as the shelf it stood on. Post drew InkBlue and happened to look about right, which is
    /// exactly why this needs a test rather than an eye: a hash that lands well by luck is
    /// indistinguishable from a choice until it doesn't.
    /// </summary>
    [Fact]
    public void EverySuiteBookOnTheHomeShelfIsBoundDeliberately()
    {
        var shelfMaterials = new HashSet<string>(BookClothPalette.Cloths
            .Where(cloth => cloth == DesignTokens.Shelf.WalnutLight));

        var hashed = HomeFeatureSet.Default.Packages
            .Where(package => package.StartsWith("com.storyworthtelling.", StringComparison.Ordinal))
            .Where(package => BookClothPalette.ForPackage(package) == BookClothPalette.For(package))
            .ToList();

        Assert.True(hashed.Count == 0,
            "These suite books take whatever cloth the hash lands on rather than a chosen "
            + "binding: " + string.Join(", ", hashed));

        // And none of ours is bound in the bookcase's own timber.
        foreach (var package in HomeFeatureSet.Default.Packages
            .Where(p => p.StartsWith("com.storyworthtelling.", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(BookClothPalette.ForPackage(package), shelfMaterials);
        }
    }

    /// <summary>
    /// The two constructions the bindings were chosen for, asserted as behaviour rather than
    /// as a hex. Plate is bound light so its marks invert to ink (Domain's Identity.Plate
    /// comment says exactly this); Post is bound deep so its marks stay cream against the
    /// warm painting. Pinning the outcome means a future cloth change that quietly flips
    /// either one has to argue with a test.
    /// </summary>
    [Fact]
    public void PlateInvertsItsMarksToInkAndPostDoesNot()
    {
        Assert.True(BookClothPalette.MarksInvertToInk(
            BookClothPalette.ForPackage(HomeFeatureSet.CameraPackage)));
        Assert.False(BookClothPalette.MarksInvertToInk(
            BookClothPalette.ForPackage(HomeFeatureSet.CommunicationPackage)));
    }

    [Fact]
    public void MarksInvertToInk_IsConsistentWithTheBindingScoreActuallyGets()
    {
        Assert.True(BookClothPalette.MarksInvertToInk(
            BookClothPalette.ForPackage(HomeFeatureSet.AudioPackage)));
    }

    /// <summary>
    /// Index is bound in the deeper green of its identity hue, by a deliberate binding rather
    /// than whatever the hash lands on (the hash is for third-party apps). The cloth is a
    /// registered token, never a literal here, and it is a dark cloth, so the shelf's marks
    /// stay cream against it.
    /// </summary>
    [Fact]
    public void IndexIsBoundInItsDeepenedIdentityGreen()
    {
        var cloth = BookClothPalette.ForPackage(PolicyContext.IndexPackage);

        Assert.Equal(DesignTokens.Identity.Index.Cloth, cloth);
        Assert.NotEqual(BookClothPalette.For(PolicyContext.IndexPackage), cloth);
        Assert.False(BookClothPalette.MarksInvertToInk(cloth));
    }

    /// <summary>
    /// Every suite app that ships an APK is a book somewhere on the shelf — the featured six at
    /// the top, the sibling apps cover-out further down — and every one of them is bound in a
    /// chosen cloth. The guard above only ever looked at the featured six, which is how Foreword
    /// shipped hashed onto the Problem role's deep tier (a maroon-brown the owner noticed on the
    /// shelf) and Folio onto a slate: neither is featured, so nothing checked them. The list is
    /// read from PolicyContext's own package constants by reflection, so the next app is covered
    /// the moment its package is named, not when somebody remembers this test.
    /// </summary>
    [Fact]
    public void EverySuiteBookAnywhereOnTheShelfIsBoundDeliberately()
    {
        var suiteBooks = SuitePackagesNamedInPolicy()
            .Where(package => package != PolicyContext.LauncherPackage)
            .ToList();
        Assert.True(suiteBooks.Count >= 7, "Expected the suite's books in PolicyContext; found " + suiteBooks.Count);

        var hashed = suiteBooks.Where(package => !BookClothPalette.HasChosenBinding(package)).ToList();

        Assert.True(hashed.Count == 0,
            "These suite books fall through to the hash rather than a chosen binding: "
            + string.Join(", ", hashed));
    }

    [Fact]
    public void ForewordIsBoundInItsOwnLightSunriseWithInkMarks()
    {
        var cloth = BookClothPalette.ForPackage(PolicyContext.ForewordPackage);

        Assert.Equal(DesignTokens.Identity.Foreword.Ground, cloth);
        Assert.True(BookClothPalette.MarksInvertToInk(cloth));
    }

    [Fact]
    public void FolioIsBoundInItsRegisteredDeepRedClothWithCreamMarks()
    {
        var cloth = BookClothPalette.ForPackage(PolicyContext.FolioPackage);

        Assert.Equal(DesignTokens.Identity.Folio.Cloth, cloth);
        Assert.False(BookClothPalette.MarksInvertToInk(cloth));
    }

    [Fact]
    public void PulpIsBoundInItsAmberClothWithInkMarks()
    {
        var cloth = BookClothPalette.ForPackage(PolicyContext.PulpPackage);

        Assert.Equal(DesignTokens.Identity.Pulp.Cloth, cloth);
        Assert.True(BookClothPalette.MarksInvertToInk(cloth));
    }

    // Pins the thin margin (D12): Constitution's cerulean cloth has Rec. 601 luma 0.635 against
    // the 0.6 threshold, so a small change to either the cloth or the formula flips the shelf's
    // rule and title from ink to cream on a light cloth. The decision is made from the hex, as
    // for every light binding; the cloth is the ladder's own Ground.
    [Fact]
    public void ConstitutionClothInvertsToInk()
    {
        var cloth = DesignTokens.Identity.Constitution.Cloth;

        Assert.Equal(DesignTokens.Identity.Constitution.Ground, cloth);
        Assert.True(BookClothPalette.MarksInvertToInk(cloth));

        var (r, g, b) = Codex.Tests.Design.DesignTestHelpers.ParseHex(cloth);
        var luma = ((0.299 * r) + (0.587 * g) + (0.114 * b)) / 255.0;
        Assert.InRange(luma, 0.63, 0.64);
    }

    private static IEnumerable<string> SuitePackagesNamedInPolicy() =>
        typeof(PolicyContext)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(value => value.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal)
                            && value.Length > PolicyContext.OwnerPackagePrefix.Length)
            .Distinct();
}
