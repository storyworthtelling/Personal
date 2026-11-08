using Folio.Infrastructure.Vault;
using Storyworthtelling.Voice.Secrets;

namespace Folio.Tests.Vault;

// Duplication audit §2: Folio wrote its reMarkable token, Claude and Gemini keys into the vault unconditionally,
// so a Folio that had never paired the tablet blanked the token Foreword published. It now follows the suite's
// rule (VaultMerge.Pick): a blank never overwrites, and a filled slot yields only to what was just changed here.
public class FolioVaultSectionTests
{
    private static readonly SecretsVault Published = SecretsVault.Empty with
    {
        Claude = new ClaudeSecrets("sk-in-vault"),
        Gemini = new GeminiSecrets("gemini-in-vault"),
        Remarkable = new RemarkableSecrets("rm-from-foreword"),
    };

    [Fact]
    public void ABlankTokenOnAFolioThatNeverPairedLeavesTheVaultsAlone()
    {
        var merged = FolioVaultSection.Merge(Published, new FolioVaultSection.Local("sk-in-vault", "gemini-in-vault", ""), [], new Dictionary<string, string>());

        Assert.Equal("rm-from-foreword", merged.Remarkable.DeviceToken);
    }

    [Fact]
    public void AnUnchangedLocalValueDoesNotReplaceTheVaults()
    {
        var merged = FolioVaultSection.Merge(Published, new FolioVaultSection.Local("sk-stale", "gemini-stale", "rm-stale"), [], new Dictionary<string, string>());

        Assert.Equal("sk-in-vault", merged.Claude.ApiKey);
        Assert.Equal("gemini-in-vault", merged.Gemini.ApiKey);
        Assert.Equal("rm-from-foreword", merged.Remarkable.DeviceToken);
    }

    [Fact]
    public void AKeyJustTypedOrATabletJustPairedReplacesTheVaults()
    {
        var merged = FolioVaultSection.Merge(
            Published,
            new FolioVaultSection.Local("sk-new", "gemini-in-vault", "rm-new"),
            [FolioVaultSection.Claude, FolioVaultSection.Remarkable],
            new Dictionary<string, string>());

        Assert.Equal("sk-new", merged.Claude.ApiKey);
        Assert.Equal("rm-new", merged.Remarkable.DeviceToken);
        Assert.Equal("gemini-in-vault", merged.Gemini.ApiKey);
    }

    [Fact]
    public void AnEmptyVaultSlotTakesWhatThisInstallHolds()
    {
        var merged = FolioVaultSection.Merge(SecretsVault.Empty, new FolioVaultSection.Local("sk-local", "g-local", "rm-local"), [], new Dictionary<string, string>());

        Assert.Equal("sk-local", merged.Claude.ApiKey);
        Assert.Equal("g-local", merged.Gemini.ApiKey);
        Assert.Equal("rm-local", merged.Remarkable.DeviceToken);
    }

    [Fact]
    public void CapturedFileIdsAreFoldedIn()
    {
        var merged = FolioVaultSection.Merge(Published, new FolioVaultSection.Local("", "", ""), [], new Dictionary<string, string> { ["x.json"] = "id-x" });

        Assert.Equal("id-x", merged.DriveFileIds["x.json"]);
    }
}
