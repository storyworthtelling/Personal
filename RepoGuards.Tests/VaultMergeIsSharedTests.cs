namespace RepoGuards.Tests;

/// <summary>
/// Duplication audit §2: three vault merge policies, and Folio's blanked the reMarkable token Foreword had
/// published. The apps' vault syncs now go through <c>VaultMerge</c>; none builds a section from its own values
/// outright or keeps a private copy of the id merge.
/// </summary>
public class VaultMergeIsSharedTests
{
    [Theory]
    [InlineData("Storyboarder/Folio.Android/VaultSync.cs")]
    [InlineData("Foreword/Foreword.Android/VaultSync.cs")]
    [InlineData("Ledger/Ledger.Android/VaultSync.cs")]
    public void NoVaultSyncKeepsItsOwnIdMerge(string file)
    {
        var source = Read(file);

        Assert.DoesNotContain("MergeIds(", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Storyboarder/Folio.Android/VaultSync.cs", "FolioVaultSection.Merge(")]
    [InlineData("Foreword/Foreword.Android/VaultSync.cs", "ForewordVaultSection.Merge(")]
    public void FolioAndForewordPublishThroughTheirTestedMerge(string file, string merge)
    {
        var source = Read(file);

        Assert.Contains(merge, source, StringComparison.Ordinal);
        foreach (var section in new[] { "new RemarkableSecrets(", "new NytSecrets(", "new MapsSecrets(", "new ClaudeSecrets(", "new GeminiSecrets(", "new ForewordSecrets(" })
        {
            Assert.DoesNotContain(section, source, StringComparison.Ordinal);
        }
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));
}
