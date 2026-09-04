using Codex.Domain.Apps;
using Codex.Domain.Design;
using Xunit;

namespace Codex.Tests.Apps;

public class BookClothPaletteTests
{
    [Fact]
    public void ForPackage_IsStable_AcrossCalls()
    {
        Assert.Equal(
            BookClothPalette.ForPackage("com.storyworthtelling.ledger"),
            BookClothPalette.ForPackage("com.storyworthtelling.ledger"));
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
        Assert.Equal(
            BookClothPalette.ForPackage("com.storyworthtelling.ledger"),
            BookClothPalette.For("com.storyworthtelling.ledger"));
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

    [Fact]
    public void MarksInvertToInk_IsConsistentWithTheBindingScoreActuallyGets()
    {
        Assert.True(BookClothPalette.MarksInvertToInk(
            BookClothPalette.ForPackage(HomeFeatureSet.AudioPackage)));
    }
}
