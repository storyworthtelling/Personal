using System.Text.RegularExpressions;
using Xunit;

namespace Folio.Tests.Guard;

/// <summary>
/// The schema reset is a <b>generation</b>, not a boolean, and its key carries no brand name.
///
/// The pivot to Canonizer dropped the database once, gated by a boolean preference. P10 drops
/// it again — a fresh start, no migration, by decision. Reusing that boolean would mean the
/// second drop never happens on the one device that matters: the owner's, where it is already
/// true. The device would then keep a database stamped with migration ids the new baseline
/// cannot build on, and Migrate() fails on a phone nobody can debug.
///
/// Storing the generation instead makes the next reset a number rather than another key, and
/// keeping the key free of brand names means the next rename does not have to touch it.
/// </summary>
public class SchemaResetGenerationTests
{
    [Fact]
    public void ResetKeyIsALiteralAndCarriesNoBrandName()
    {
        var store = FolioInvariantsTests.SettingsStoreSource();

        // The CURRENT generation's key. The pivot's own key is frozen history and still carries the old
        // brand on purpose — it is asserted separately, below, because a device is recognised by it.
        var key = Regex.Match(store, @"const string SchemaResetGeneration\s*=\s*""(?<key>[^""]+)""");
        Assert.True(key.Success,
            "The schema reset's key must be declared as a string literal constant named "
            + "SchemaResetGeneration, so a property rename cannot move the key.");

        Assert.DoesNotContain("nameof", key.Value, StringComparison.Ordinal);

        // The key outlives the brand. It has already survived one rename by accident; the next one
        // should not need a migration either.
        foreach (var brand in new[] { "Canonizer", "Folio", "Storyboarder" })
        {
            Assert.DoesNotContain(brand, key.Groups["key"].Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ResetIsAGenerationAheadOfThePivot()
    {
        var store = FolioInvariantsTests.SettingsStoreSource();

        var current = Regex.Match(store, @"CurrentSchemaResetGeneration\s*=\s*(?<n>\d+)");
        Assert.True(current.Success,
            "The settings store must declare CurrentSchemaResetGeneration, so a later reset is a "
            + "number rather than another preference key.");

        Assert.True(int.Parse(current.Groups["n"].Value) >= 2,
            "The pivot's reset was generation 1. P10's fresh start has to be a later generation, or "
            + "the drop never runs on a device that already did the first one.");
    }

    // A device that did the pivot's reset must be read as generation 1, not as 'never reset' —
    // otherwise the old boolean is ignored and the generation ladder starts again from zero.
    [Fact]
    public void ThePivotsBooleanIsStillReadOnUpgrade()
    {
        var store = FolioInvariantsTests.SettingsStoreSource();

        Assert.Contains("\"CanonizerSchemaReset\"", store, StringComparison.Ordinal);
    }
}
