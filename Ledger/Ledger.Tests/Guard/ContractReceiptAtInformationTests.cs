using System.Text.RegularExpressions;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// A contract broadcast that arrived is the ordinary case, so each of Index's contract receivers logs its
/// <c>ContractBroadcast.ReceiptLine</c> at Information. Logged at Warning, every normal receipt reads in the
/// weekly log digest as a problem and buries the real ones. Each receiver's receipt line goes through a
/// helper; this reads the receiver's source and follows the call to that helper's body.
/// </summary>
public class ContractReceiptAtInformationTests
{
    [Theory]
    [InlineData("BalanceReceiver.cs")]
    [InlineData("UsageReceiver.cs")]
    public void TheReceiptLine_IsLoggedAtInformation(string file)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Ledger", "Ledger.Android", "Platforms", "Android", file));

        var call = Regex.Match(source, @"(\w+)\(\s*ContractBroadcast\.ReceiptLine\(");
        Assert.True(call.Success, $"{file} no longer logs ContractBroadcast.ReceiptLine.");
        var helper = call.Groups[1].Value;

        var body = Regex.Match(
            source,
            @"private static void " + Regex.Escape(helper) + @"\([^)]*\)\s*(=>[^;]*;|\{(?:[^{}]|\{[^{}]*\})*\})",
            RegexOptions.Singleline);
        Assert.True(body.Success, $"{file}: couldn’t find the body of {helper}.");
        Assert.Contains("LogInformation(", body.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("LogWarning(", body.Value, StringComparison.Ordinal);
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
