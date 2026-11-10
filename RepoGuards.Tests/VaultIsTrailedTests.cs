using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// Every head's vault reports its restores and publishes to the head's log (audit 08 §6–8). They were swallowed, so
/// after a factory reset a mistyped passphrase read exactly like "nothing to restore".
/// </summary>
public class VaultIsTrailedTests
{
    // Constitution writes every vault restore and publish to its own vault trail already (ConstitutionVaultSync's
    // phrases, at WARN when they name a failure), and the role reader only asks who owns the file for that same sync.
    private static readonly string[] Exempt =
    [
        "Constitution/Constitution.Infrastructure/Vault/ConstitutionVaultSync.cs",
        "Constitution/Constitution.Infrastructure/Household/HouseholdRoleReader.cs",
    ];

    [Fact]
    public void EveryHeadThatBuildsAVaultGivesItTheLog()
    {
        var found = Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(path => (path, relative: Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/')))
            .Where(file => !file.relative.StartsWith("Storyworthtelling.Voice/", StringComparison.Ordinal))
            .Where(file => !Exempt.Contains(file.relative))
            .Select(file => (file.relative, text: File.ReadAllText(file.path)))
            .Where(file => Regex.Matches(file.text, @"new (SecretsVaultService|PersonalVaultAccess)\(").Count
                > Regex.Matches(file.text, @"Trail\(""Vault""\)").Count)
            .Select(file => file.relative)
            .ToList();

        Assert.True(found.Count == 0, "Pass the head's log as the vault's trail (HeadLog.Trail / MauiLog.Trail):\n" + string.Join("\n", found));
    }
}
