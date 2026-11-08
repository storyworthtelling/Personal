using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice.Tests.Secrets;

// Duplication audit §2: three merge policies for one vault. Folio wrote its blank reMarkable token over the one
// Foreword paired, and Foreword wrote its NYT, reMarkable and Maps values unconditionally; Index and
// Constitution each had a guard of their own. One rule for every app: a blank never overwrites, a slot the vault
// leaves empty takes what this install holds, and a filled slot is replaced only by a value just changed here.
public class VaultMergeTests
{
    [Theory]
    [InlineData("", "in-vault", false, "in-vault")]
    [InlineData("", "in-vault", true, "in-vault")]
    [InlineData("local", "", false, "local")]
    [InlineData("local", "in-vault", false, "in-vault")]
    [InlineData("local", "in-vault", true, "local")]
    [InlineData("", "", true, "")]
    public void ABlankNeverOverwritesAndAFilledSlotYieldsOnlyToAChange(string local, string inVault, bool changed, string expected)
    {
        Assert.Equal(expected, VaultMerge.Pick(local, inVault, changed));
    }

    [Theory]
    [InlineData(0, 41.5, false, 41.5)]
    [InlineData(40.1, 0, false, 40.1)]
    [InlineData(40.1, 41.5, false, 41.5)]
    [InlineData(40.1, 41.5, true, 40.1)]
    public void ANumberFollowsTheSameRuleWithZeroAsBlank(double local, double inVault, bool changed, double expected)
    {
        Assert.Equal(expected, VaultMerge.Pick(local, inVault, changed));
    }

    [Fact]
    public void CapturedIdsWinPerNameAndAForgottenIdNeverErasesOne()
    {
        var existing = new Dictionary<string, string> { ["a.json"] = "id-a", ["b.json"] = "id-b-old" };
        var captured = new Dictionary<string, string> { ["b.json"] = "id-b-new", ["c.json"] = "id-c", ["a.json"] = "" };

        var merged = VaultMerge.MergeDriveFileIds(existing, captured);

        Assert.Equal("id-a", merged["a.json"]);
        Assert.Equal("id-b-new", merged["b.json"]);
        Assert.Equal("id-c", merged["c.json"]);
    }
}
