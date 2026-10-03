using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guard G26 (D158, mockup 14 state 2): when the vault holds banks and no passphrase is set, the
/// pinned bar's button must not say "Add an account" — adding one there would double-link a bank the
/// vault already holds and spend one of Plaid's ten Trial Items (D149).
///
/// <para>Read by source text, like every other guard in this folder that cannot load
/// <c>Ledger.Android</c> (a <c>net10.0-android</c> project): it checks that
/// <c>UpdateBarButton</c>'s <c>vaultHasUnreadBanks</c> branch is wired to a DIFFERENT label than the
/// ordinary one, and that the first-run vault-holds-banks branch actually calls it with
/// <c>true</c> — the two facts that together make the button say the right thing in that state.</para>
/// </summary>
public class NoDoubleLinkTests
{
    [Fact]
    public void UpdateBarButton_OffersADifferentLabel_WhenTheVaultHoldsUnreadBanks()
    {
        var source = MainPageSource();

        var method = ExtractMethod(source, "private void UpdateBarButton(bool vaultHasUnreadBanks = false)");
        Assert.Contains("\"Enter the passphrase\"", method, StringComparison.Ordinal);
        Assert.Contains("\"Add an account\"", method, StringComparison.Ordinal);

        // The two labels must be on OPPOSITE sides of the vaultHasUnreadBanks branch, not both
        // reachable regardless of it — a ternary keyed on the parameter is what this actually checks.
        Assert.Matches(
            "vaultHasUnreadBanks\\s*\\?\\s*\"Enter the passphrase\"\\s*:\\s*\"Add an account\"", method);
    }

    [Fact]
    public void FirstRun_PassesTrue_WhenTheVaultHoldsBanksAndNoPassphraseIsSet()
    {
        var source = MainPageSource();
        var method = ExtractMethod(source, "private async Task RenderFirstRunAsync()");

        Assert.Contains("UpdateBarButton(vaultHasUnreadBanks)", method, StringComparison.Ordinal);
        // vaultHasUnreadBanks must itself be conditioned on there being no passphrase set — otherwise
        // this would also fire once banks ARE restored and readable, which is the ordinary case.
        Assert.Contains("VaultPassphrase", method, StringComparison.Ordinal);
    }

    private static string MainPageSource()
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Android", "Pages", "MainPage.xaml.cs");
        Assert.True(File.Exists(path), $"MainPage.xaml.cs not found at {path}.");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// A crude but sufficient brace-matched extraction of one method body — good enough for a guard
    /// whose job is "does this text appear inside that method", not a parser. Takes the full
    /// declaration line (access modifier, return type, parameters) rather than a bare method name: a
    /// bare name matches the first CALL site too if one happens to appear earlier in the file, which
    /// is exactly the wrong occurrence to check.
    /// </summary>
    internal static string ExtractMethod(string source, string declaration)
    {
        var nameIndex = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Declaration not found in MainPage.xaml.cs: {declaration}");

        var braceStart = source.IndexOf('{', nameIndex);
        var depth = 0;
        var i = braceStart;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) break;
            }
        }

        return source[braceStart..(i + 1)];
    }
}
