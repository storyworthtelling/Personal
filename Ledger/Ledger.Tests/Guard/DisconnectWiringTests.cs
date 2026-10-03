using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Source-level half of Guard R-G11 (the behavioural half is <c>AccountServiceDisconnectTests</c>): the
/// Android head cannot be loaded here, so the wiring that makes the behaviour reachable is read from the
/// files. Before the fix the head built its undo from the bank's Plaid credentials alone, and called
/// Plaid with whatever token the row held — blank for a hand-entered bank.
/// </summary>
public class DisconnectWiringTests
{
    [Fact]
    public void TheAccountsPageDisconnectsAndUndoesThroughTheSnapshot()
    {
        var main = Source("Ledger.Android/Pages/MainPage.xaml.cs");

        Assert.Contains("accounts.DisconnectInstitutionAsync(", main, StringComparison.Ordinal);
        Assert.Contains("accounts.UndoDisconnectAsync(", main, StringComparison.Ordinal);
        Assert.Contains("accounts.CommitDisconnectAsync(", main, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreInstitutionsAsync", main, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveInstitutionAsync", main, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ledger.Android/Pages/MainPage.xaml.cs")]
    [InlineData("Ledger.Android/Pages/SetupPage.xaml.cs")]
    public void NoPageCallsItemRemoveDirectly_SoABlankTokenCanNeverReachPlaid(string ledgerRelativePath)
    {
        var text = Source(ledgerRelativePath);

        Assert.DoesNotContain(".RemoveItemAsync(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsReleasesThroughTheSharedHelper()
    {
        Assert.Contains("PlaidItemRelease.ReleaseAsync(", Source("Ledger.Android/Pages/SetupPage.xaml.cs"), StringComparison.Ordinal);
    }

    private static string Source(string ledgerRelativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), ledgerRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {ledgerRelativePath}");
        return File.ReadAllText(path);
    }
}
