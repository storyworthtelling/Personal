using System.Text.RegularExpressions;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// The repository is public. The owner's recorded Debt Snowball history is their finances, so it lives in
/// their own Drive, encrypted, and is read at runtime — exactly like the household snapshot. These guards
/// keep it that way: a file dropped back into <c>design/</c>, an <c>EmbeddedResource</c> that ships it in
/// the APK, a reader that looks for it inside the assembly, or a Drive source that is handed the
/// household file by mistake would each put it back in the source or break the import without a test failing
/// anywhere else.
/// </summary>
public class SnowballHistoryNotInTheRepoTests
{
    [Fact]
    public void NoJsonDataFileSitsInTheDesignFolder()
    {
        var design = Path.Combine(DesignTestHelpers.LedgerRoot(), "design");

        Assert.Empty(Directory.GetFiles(design, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void NoProjectEmbedsAHistoryFileExceptTheInventedTestFixture()
    {
        var offenders = new List<string>();
        foreach (var project in Directory.GetFiles(DesignTestHelpers.LedgerRoot(), "*.csproj", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(project);
            foreach (Match item in Regex.Matches(text, @"<EmbeddedResource\s+Include=""(?<path>[^""]+)""", RegexOptions.IgnoreCase))
            {
                var path = item.Groups["path"].Value;
                var looksLikeHistory = path.Contains("history", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                if (looksLikeHistory && !Path.GetFileName(path.Replace('\\', '/')).StartsWith("synthetic-", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(project)}: {path}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "A history file is embedded in a build: " + string.Join("; ", offenders));
    }

    [Fact]
    public void TheShippedSourceHasNoFixtureReaderAndLooksInsideNoAssemblyForData()
    {
        var offenders = new List<string>();
        foreach (var project in new[] { "Ledger.Domain", "Ledger.Infrastructure", "Ledger.Android" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(DesignTestHelpers.LedgerRoot(), project), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            {
                var text = File.ReadAllText(file);
                if (text.Contains("ReadHistoryFixture", StringComparison.Ordinal)
                    || text.Contains("GetManifestResourceStream", StringComparison.Ordinal)
                    || text.Contains("design/snowball-history", StringComparison.Ordinal))
                {
                    offenders.Add(Path.GetRelativePath(DesignTestHelpers.LedgerRoot(), file));
                }
            }
        }

        Assert.True(offenders.Count == 0, "Reads shipped data: " + string.Join("; ", offenders));
    }

    // ---- the Drive source is wired to its OWN file ----

    [Fact]
    public void MauiProgramConstructsTheHistorySourceWithItsOwnFileName()
    {
        var program = File.ReadAllText(Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Android/MauiProgram.cs"));

        var construction = Regex.Match(
            program,
            @"new DriveSnowballHistorySource\(\s*new DriveTextFile\(\s*DriveFactory\(sp\),\s*DriveSnowballHistorySource\.FileName,",
            RegexOptions.Singleline);
        Assert.True(construction.Success, "MauiProgram does not build DriveSnowballHistorySource over a DriveTextFile named DriveSnowballHistorySource.FileName.");
    }

    [Fact]
    public void ThereIsStillExactlyOneBareDriveTextFileRegistrationAndItIsTheHouseholdsOwn()
    {
        var program = File.ReadAllText(Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Android/MauiProgram.cs"));

        // A second bare IDriveTextFile singleton would make DI hand whichever registration came last to
        // anything asking for one — the importer would be reading the household file.
        var registrations = Regex.Matches(program, @"Add(Singleton|Scoped|Transient)<IDriveTextFile>");
        Assert.Single(registrations);
        Assert.Matches(
            new Regex(@"Add(Singleton|Scoped|Transient)<IDriveTextFile>\(sp\s*=>\s*new DriveTextFile\(\s*DriveFactory\(sp\),\s*HouseholdSnapshotReader\.FileName", RegexOptions.Singleline),
            program);
    }

    [Theory]
    [InlineData("Ledger.Infrastructure/Services/SnowballService.cs")]
    [InlineData("Ledger.Infrastructure/Snowball/SnowballHistoryImport.cs")]
    public void TheImporterNeverTakesABareDriveTextFile(string path)
    {
        var source = File.ReadAllText(Path.Combine(DesignTestHelpers.LedgerRoot(), path));

        Assert.DoesNotContain("IDriveTextFile", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheImportIsWiredToTheVaultPassphraseAndToTheInstallRole()
    {
        var program = File.ReadAllText(Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Android/MauiProgram.cs"));

        var construction = Regex.Match(program, @"new SnowballHistoryImport\((?<args>[^;]*)\);", RegexOptions.Singleline);
        Assert.True(construction.Success, "MauiProgram no longer builds a SnowballHistoryImport.");
        Assert.Contains("SettingsStore.VaultPassphrase", construction.Groups["args"].Value, StringComparison.Ordinal);
        Assert.Contains("InstallRoleService", construction.Groups["args"].Value, StringComparison.Ordinal);
    }
}
