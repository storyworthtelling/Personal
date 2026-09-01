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
}
