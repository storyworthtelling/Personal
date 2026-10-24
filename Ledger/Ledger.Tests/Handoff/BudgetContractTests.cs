using System.Text.RegularExpressions;
using Index.Domain.Handoff;
using Xunit;

namespace Index.Tests.Handoff;

/// <summary>
/// The budget contract's wire, pinned on Index's side and, by content, against Constitution's own constants
/// (any <c>Constitution/**/*.cs</c> holding the same four literals). Index only SENDS: it uses the
/// permission Constitution declares, so nothing here expects a receiver in Index.
/// </summary>
public class BudgetContractTests
{
    [Fact]
    public void TheWireIsPinned()
    {
        Assert.Equal("com.storyworthtelling.constitution", ConstitutionBudgetContract.Package);
        Assert.Equal("com.storyworthtelling.constitution.BudgetReceiver", ConstitutionBudgetContract.Receiver);
        Assert.Equal("com.storyworthtelling.constitution.action.BUDGET", ConstitutionBudgetContract.Action);
        Assert.Equal("com.storyworthtelling.constitution.RECEIVE_BUDGET", ConstitutionBudgetContract.Permission);
        Assert.Equal("month", ConstitutionBudgetContract.MonthExtra);
        Assert.Equal("budget", ConstitutionBudgetContract.BudgetExtra);
        Assert.Equal("spent", ConstitutionBudgetContract.SpentExtra);
    }

    [Fact]
    public void IndexUsesButNeverDeclaresThePermission()
    {
        var manifest = File.ReadAllText(Path.Combine(AndroidDir(), "Platforms", "Android", "AndroidManifest.xml"));
        Assert.DoesNotContain("RECEIVE_BUDGET", manifest, StringComparison.Ordinal);

        var uses = File.ReadAllText(Path.Combine(AndroidDir(), "ConstitutionBudgetPermission.cs"));
        Assert.Contains("[assembly: UsesPermission(Name = ConstitutionBudgetContract.Permission)]", uses, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstitutionsConstantsMatchByContent()
    {
        var path = Path.Combine(RepoRoot(), "Constitution", "Constitution.Domain", "ConstitutionIdentity.cs");
        Assert.True(File.Exists(path), "Constitution's identity file is missing (the receiving side has not landed).");
        var source = File.ReadAllText(path);

        Assert.Equal(ConstitutionBudgetContract.Package, Constant(source, "ApplicationId"));
        Assert.Equal(ConstitutionBudgetContract.Receiver, ConstitutionBudgetContract.Package + Suffix(source, "BudgetReceiverName"));
        Assert.Equal(ConstitutionBudgetContract.Action, ConstitutionBudgetContract.Package + Suffix(source, "BudgetAction"));
        Assert.Equal(ConstitutionBudgetContract.Permission, ConstitutionBudgetContract.Package + Suffix(source, "BudgetPermission"));
    }

    [Fact]
    public void ConstitutionReadsTheSameThreeExtras()
    {
        var receiver = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Constitution"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText)
            .Where(text => text.Contains("BudgetReceiverName", StringComparison.Ordinal)
                        && text.Contains("\"spent\"", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(receiver);
        Assert.Contains(receiver, text => text.Contains("\"month\"", StringComparison.Ordinal)
                                       && text.Contains("\"budget\"", StringComparison.Ordinal));
    }

    private static string Constant(string source, string name)
    {
        var match = Regex.Match(source, name + @"\s*=\s*""([^""]+)""");
        Assert.True(match.Success, $"{name} not found in Constitution's identity file.");
        return match.Groups[1].Value;
    }

    private static string Suffix(string source, string name)
    {
        var match = Regex.Match(source, name + @"\s*=\s*ApplicationId\s*\+\s*""([^""]+)""");
        Assert.True(match.Success, $"{name} is not ApplicationId + a literal in Constitution's identity file.");
        return match.Groups[1].Value;
    }

    private static string AndroidDir() => Path.Combine(RepoRoot(), "Ledger", "Ledger.Android");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found from the test output directory.");
    }
}
