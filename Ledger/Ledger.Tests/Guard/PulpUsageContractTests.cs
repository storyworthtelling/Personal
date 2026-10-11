using System.Text.RegularExpressions;
using Index.Domain.Identity;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guard G16, Index's half: <c>UsageReceiver</c>'s declared shape (its C# attributes and the manifest's
/// permission) against the constants of Pulp's own contract (<c>Pulp/Pulp.Domain/Usage/IndexUsageContract.cs</c>),
/// read by content so a change on Pulp's side fails here instead of Index answering to an action nobody
/// sends. Pulp's half (<c>Pulp.Tests/Usage/IndexUsageContractTests</c>) reads Index's receiver the same way.
/// Expected values are checked against <see cref="IndexIdentity.ApplicationId"/>, never retyped.
/// </summary>
public class PulpUsageContractTests
{
    [Fact]
    public void Package_MatchesIndexIdentitysApplicationId()
    {
        Assert.Equal(IndexIdentity.ApplicationId, ContractConstant("Package"));
    }

    [Fact]
    public void Receiver_IsTheClassNamedInTheContract()
    {
        var suffix = SuffixAfterApplicationId(ContractConstant("Receiver"));
        Assert.Contains($"Name = IndexIdentity.ApplicationId + \"{suffix}\"", ReceiverSource(), StringComparison.Ordinal);
    }

    [Fact]
    public void Action_IsDeclaredOnTheReceiverAndItsIntentFilter()
    {
        var suffix = SuffixAfterApplicationId(ContractConstant("Action"));
        var receiver = ReceiverSource();
        Assert.Contains($"HandoffAction = IndexIdentity.ApplicationId + \"{suffix}\";", receiver, StringComparison.Ordinal);
        Assert.Contains("[IntentFilter([UsageReceiver.HandoffAction])]", receiver, StringComparison.Ordinal);
    }

    [Fact]
    public void Permission_GuardsTheReceiver_AndIsDeclaredAtSignatureLevel()
    {
        var full = ContractConstant("Permission");
        var suffix = SuffixAfterApplicationId(full);
        Assert.Contains($"Permission = IndexIdentity.ApplicationId + \"{suffix}\"", ReceiverSource(), StringComparison.Ordinal);
        Assert.Contains(
            $"<permission android:name=\"{full}\" android:protectionLevel=\"signature\" />",
            File.ReadAllText(ManifestPath()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ServiceExtra")]
    [InlineData("MonthExtra")]
    [InlineData("CountExtra")]
    [InlineData("LastOpenedExtra")]
    [InlineData("MerchantsExtra")]
    public void EveryPayloadFieldName_IsTheOnePulpSends(string name)
    {
        var value = ContractConstant(name);
        Assert.Contains($"public const string {name} = \"{value}\";", ReceiverSource(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMerchantSeparator_IsTheOnePulpJoinsWith()
    {
        var match = Regex.Match(ContractSource(), @"MerchantSeparator\s*=\s*'(.)'");
        Assert.True(match.Success);
        Assert.Contains($"MerchantSeparator = '{match.Groups[1].Value}';", File.ReadAllText(DomainParserPath()), StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiverSource_ParsesThroughTheDomainAndStoresTheResult()
    {
        var receiver = ReceiverSource();
        Assert.Contains("PulpUsageReceipt.TryParse(", receiver, StringComparison.Ordinal);
        Assert.Contains(".Apply(", receiver, StringComparison.Ordinal);
    }

    // ---- helpers ----

    private static string SuffixAfterApplicationId(string fullValue)
    {
        Assert.StartsWith(IndexIdentity.ApplicationId, fullValue, StringComparison.Ordinal);
        return fullValue[IndexIdentity.ApplicationId.Length..];
    }

    private static string ReceiverSource()
    {
        var path = Path.Combine(RepoRoot(), "Ledger", "Ledger.Android", "Platforms", "Android", "UsageReceiver.cs");
        Assert.True(File.Exists(path), $"UsageReceiver.cs not found at {path}.");
        return File.ReadAllText(path);
    }

    private static string ManifestPath() =>
        Path.Combine(RepoRoot(), "Ledger", "Ledger.Android", "Platforms", "Android", "AndroidManifest.xml");

    private static string DomainParserPath() =>
        Path.Combine(RepoRoot(), "Ledger", "Ledger.Domain", "Usage", "PulpUsageReceipt.cs");

    private static string ContractSource()
    {
        var path = Path.Combine(RepoRoot(), "Pulp", "Pulp.Domain", "Usage", "IndexUsageContract.cs");
        Assert.True(File.Exists(path), $"IndexUsageContract.cs not found at {path} — is Pulp checked out?");
        return File.ReadAllText(path);
    }

    private static string ContractConstant(string name)
    {
        var match = Regex.Match(ContractSource(), $@"public const string {Regex.Escape(name)}\s*=\s*""([^""]*)""\s*;");
        Assert.True(match.Success, $"Could not find {name}'s literal value in IndexUsageContract.cs.");
        return match.Groups[1].Value;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
