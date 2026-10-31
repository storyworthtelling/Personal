namespace LogLoopReader.Tests;

// §6.1 layer 4. The session's scan: a token of 4+ characters that the week's plaintext holds and the
// repository did not already contain blocks the push — and the hit names where it was, never the token.
// CI's content-free subset runs on every claude/** push without any plaintext. Sample values are built by
// concatenation so this file itself never carries one whole.
public class LogLoopLeakScanTests
{
    private static readonly string[] Repo = ["public void LoadOpenThread() { Sync failed; }", "Mailbox Settings Save"];

    [Fact]
    public void ATokenFromThePlaintextThatTheRepoNeverHadIsAHit()
    {
        var scan = LeakScan.Build(["Sync failed for Juniper Halloway, balance 1204"], Repo);

        var hits = scan.Scan("+    Assert.Equal(\"Halloway\", name);", "diff");

        var hit = Assert.Single(hits);
        Assert.Equal("diff", hit.Where);
        Assert.Equal(1, hit.Line);
        Assert.DoesNotContain("Halloway", hit.ToString());
    }

    [Fact]
    public void TokensTheRepoAlreadyHasAreNotHitsEvenIfTheLogsHaveThem()
    {
        var scan = LeakScan.Build(["Sync failed in LoadOpenThread on Mailbox"], Repo);

        Assert.Empty(scan.Scan("LoadOpenThread now handles a missing Mailbox row; Sync retries", "diff"));
    }

    [Fact]
    public void ShortTokensAreIgnored()
    {
        var scan = LeakScan.Build(["the cat sat on a mat"], Repo);

        Assert.Empty(scan.Scan("cat mat sat", "body"));
    }

    [Fact]
    public void MatchingIsCaseInsensitiveSoACapitalisedCopyStillHits()
    {
        var scan = LeakScan.Build(["sent to halloway"], Repo);

        Assert.Single(scan.Scan("HALLOWAY", "title"));
    }

    [Fact]
    public void TheSetHoldsSaltedHashesNotTheTokens()
    {
        var scan = LeakScan.Build(["Juniper Halloway"], Repo);

        Assert.DoesNotContain("Halloway", scan.DescribeForDisk());
    }

    [Fact]
    public void CiFlagsACardLikeNumber()
    {
        var card = "4111" + " 1111 " + "1111 " + "1111";
        var hits = CiLeakScan.Scan("Tests/X.cs", [(3, $"var c = \"{card}\";")], logfixBranch: false);

        Assert.Equal("card-number", Assert.Single(hits).Rule);
    }

    [Fact]
    public void CiAllowsExampleAddressesAndFlagsOthers()
    {
        var real = "ada" + "@" + "mail" + ".test-domain.net";
        Assert.Empty(CiLeakScan.Scan("a.cs", [(1, "x = \"ada@example.com\"; y = \"z@mail.example.org\";")], false));
        Assert.Empty(CiLeakScan.Scan("a.cs", [(1, "Co-Authored-By: Claude <noreply@anthropic.com>")], false));
        Assert.Equal("email", Assert.Single(CiLeakScan.Scan("a.cs", [(1, $"x = \"{real}\";")], false)).Rule);
    }

    [Fact]
    public void CiAllowsFiveFiveFiveZeroOneNumbersAndFlagsOthers()
    {
        var real = "(312) " + "867-" + "5309";
        Assert.Empty(CiLeakScan.Scan("a.cs", [(1, "call 555-0142 or (555) 010-0199")], false));
        Assert.Equal("phone", Assert.Single(CiLeakScan.Scan("a.cs", [(1, $"call {real}")], false)).Rule);
    }

    [Fact]
    public void CiFlagsOauthShapedStrings()
    {
        var google = "4/" + "0AeanS0b" + "XyZ12345abcdeFGHIJklmnop";
        var plaid = "public-" + "sandbox-" + "0f8c1c2a-3b4d-4e5f-8a9b-0c1d2e3f4a5b";
        var refresh = "1//" + "0gAbCdEfGhIjKlMnOpQrStUvWx";

        Assert.All(new[] { google, plaid, refresh }, token =>
            Assert.Equal("oauth-code", Assert.Single(CiLeakScan.Scan("a.cs", [(1, $"\"{token}\"")], false)).Rule));
        Assert.Empty(CiLeakScan.Scan("a.cs", [(1, "code=<redacted> state=xyz")], false));
    }

    [Fact]
    public void CiFlagsDollarAmountsInTestsOnALogFixBranchOnly()
    {
        var line = (1, "Assert.Equal(\"$1,204.57\", balance);");

        Assert.Equal("amount-in-test", Assert.Single(CiLeakScan.Scan("Ledger/Ledger.Tests/X.cs", [line], logfixBranch: true)).Rule);
        Assert.Empty(CiLeakScan.Scan("Ledger/Ledger.Tests/X.cs", [line], logfixBranch: false));
        Assert.Empty(CiLeakScan.Scan("Ledger/Ledger.Domain/X.cs", [line], logfixBranch: true));
    }

    [Fact]
    public void AddedLinesAreReadFromAUnifiedDiff()
    {
        const string diff = "diff --git a/x.cs b/x.cs\n--- a/x.cs\n+++ b/x.cs\n@@ -10,3 +10,4 @@ class X\n context\n-removed\n+added one\n+added two\n context\n";

        var added = DiffLines.Added(diff).ToList();

        Assert.Equal([("x.cs", 11, "added one"), ("x.cs", 12, "added two")], added);
    }
}
