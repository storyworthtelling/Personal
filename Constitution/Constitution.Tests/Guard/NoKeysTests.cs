using System.Text.RegularExpressions;

namespace Constitution.Tests.Guard;

/// <summary>
/// C-12.2.1 (G23) — no key in Constitution's source. The repository is public and a key pushed
/// once stays retrievable by commit hash, so keys live in the encrypted Drive vault and Actions
/// secrets, never in code, config, fixtures or the plan/notes/spikes/progress docs.
///
/// <para>Modelled on Pulp's <c>NoKeysTests</c>, with three differences. It reads markdown too
/// (a key pasted into a spike write-up is as public as one in code). It reports file and line,
/// never the matched text, so a failing run on a real key does not become a second place the key
/// is published. And it is non-vacuous in three ways: the scan must cover a floor of files
/// including the named docs, every detector shape must fire on an in-memory sample, and a
/// planted bad file in a temp directory must come back named by file and line.</para>
///
/// <para><b>What it cannot catch:</b> a key with no recognisable shape (a short or word-like
/// secret, a diceware passphrase in prose), a bare 40-hex run (indistinguishable from a git
/// commit id) unless a key word is on the same line, and a secret split across lines. Those are
/// review's job.</para>
/// </summary>
public class NoKeysTests
{
    private const int FileFloor = 12;

    private const char SingleQuote = '\'';
    private const char Backtick = '`';

    [Fact]
    public void NoKeyShapedLiteralUnderConstitution()
    {
        var result = GuardScan.Run(ConstitutionSource.Root(), (path, text) => KeyShapes.Find(text));
        AssertScanned(result);

        // The match itself is never printed: file, line and the kind of shape only.
        Assert.True(result.Hits.Count == 0,
            "A key-shaped string is under Constitution/; keys belong in the vault:\n"
            + string.Join("\n", result.Hits));
    }

    [Fact]
    public void TheScanCoversSourceConfigAndTheDocs()
    {
        AssertScanned(GuardScan.Run(ConstitutionSource.Root(), (path, text) => []));
    }

    [Fact]
    public void ThisGuardIsScannedLikeEverythingElse()
    {
        // No exclusions: the samples below are built at run time, so this file holds no key shape.
        var result = GuardScan.Run(ConstitutionSource.Root(), (path, text) => []);
        Assert.Contains("Constitution.Tests/Guard/NoKeysTests.cs", result.Scanned);
        Assert.Contains("Constitution.Tests/Guard/InventedDataTests.cs", result.Scanned);
    }

    public static IEnumerable<object[]> KeySamples()
    {
        yield return ["Google API key", "var key = \"" + "AIza" + new string('Q', 35) + "\";"];
        yield return ["secret-key token", "key: " + "sk-" + new string('q', 40)];
        yield return ["secret-key token", "key: " + "sk-" + "ant-" + new string('q', 30)];
        yield return ["GitHub token", "token " + "ghp_" + new string('Z', 36)];
        yield return ["GitHub token", "token " + "github_pat_" + new string('Z', 30)];
        yield return ["AWS access key id", "id " + "AKIA" + new string('Q', 16)];
        yield return ["Slack token", "hook " + "xoxb-" + new string('7', 12) + "-abc"];
        yield return ["private key block", "-----" + "BEGIN " + "PRIVATE KEY-----"];
        yield return ["private key block", "-----" + "BEGIN " + "RSA PRIVATE KEY-----"];
        yield return ["quoted hex run", "\"" + string.Concat(Enumerable.Repeat("0a1b", 8)) + "\""];
        yield return ["quoted hex run", "'" + string.Concat(Enumerable.Repeat("0a1b", 16)) + "'"];
        yield return ["hex value near a key word", "The API " + "key is " + string.Concat(Enumerable.Repeat("9f3c", 8)) + " for now"];
        yield return ["quoted long token", "\"" + string.Concat(Enumerable.Repeat("aB3", 14)) + "\""];
        yield return ["long token", "Use " + string.Concat(Enumerable.Repeat("aB3dE", 8)) + " in the header"];
        yield return ["secret assignment", "var " + "Pass" + "word = \"" + "hunter" + "2\";"];
        yield return ["secret assignment", "Pass" + "phrase = \"" + "maple river tin" + "\""];
        yield return ["secret assignment", "\"Refresh" + "Token\": \"" + "abc" + "123\""];
        yield return ["secret assignment", string.Concat("client_", "secret", ": ", SingleQuote, "s3cr3t", SingleQuote)];
        yield return ["secret assignment", "api" + "key = " + Backtick + "zzzz" + Backtick];
    }

    /// <summary>The detector itself, against invented samples assembled at run time.</summary>
    [Theory]
    [MemberData(nameof(KeySamples))]
    public void TheDetectorRecognisesAKeyShape(string kind, string sample)
    {
        var findings = KeyShapes.Find("first line\n" + sample + "\nlast line");
        Assert.Contains(findings, finding => finding.Kind == kind && finding.Line == 2);
    }

    public static IEnumerable<object[]> OrdinarySamples()
    {
        yield return ["public void TheDetectorRecognisesAQuotedHexKeyInSourceFiles()"];
        yield return ["\"com.example.sample.WeeklyRefreshWorker\""];
        yield return ["<PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.12.0\" />"];
        yield return ["Constitution.Tests/Guard/ConstitutionInvariantsTests.cs"];
        yield return ["`Tools/IconStudio/Tokens.cs` and `Storyworthtelling.Paper/AppIdentityTokens.cs`"];
        yield return ["the header rule is `#476F83` on the muted ink"];
        yield return ["Pass" + "phrase = \"\""];
        yield return ["Pass" + "phrase = \"invented sample passphrase\""];
        yield return ["Pass" + "phrase = \"" + "example-" + "only\""];
        yield return ["token: \"${{ secrets.GITHUB_TOKEN }}\""];
        yield return ["the vault passphrase: entered by hand on each phone, never stored"];
        yield return ["`SecretsVault.Constitution` with `[JsonPropertyName(\"Constitution\")]`"];
        yield return ["Sleep Number login and the reMarkable device token live in the vault"];
        yield return ["actions/checkout@v4 and a 40-hex commit id " + string.Concat(Enumerable.Repeat("ab12", 10))];
        yield return ["sha256 of the cover is " + string.Concat(Enumerable.Repeat("0a1b2c3d", 8))];
        yield return ["a tight ladder of risk-adjusted task-lists"];
    }

    [Theory]
    [MemberData(nameof(OrdinarySamples))]
    public void TheDetectorIgnoresOrdinaryText(string line)
    {
        Assert.Empty(KeyShapes.Find(line));
    }

    [Fact]
    public void APlantedKeyIsReportedByFileAndLine()
    {
        var root = GuardScan.TempRoot();
        try
        {
            var key = "AIza" + new string('Q', 35);
            GuardScan.Plant(root, "Fixtures/sample.json", "{\n  \"note\": \"fine\",\n  \"k\": \"" + key + "\"\n}\n");
            GuardScan.Plant(root, "docs/notes.md", "# Notes\n\nNothing here.\n");
            GuardScan.Plant(root, "bin/Debug/ignored.json", "\"" + key + "\"");

            var result = GuardScan.Run(root.FullName, (path, text) => KeyShapes.Find(text));

            Assert.Equal(["Fixtures/sample.json", "docs/notes.md"], result.Scanned);
            var hit = Assert.Single(result.Hits);
            Assert.Equal("Fixtures/sample.json:3: Google API key", hit.ToString());
            Assert.DoesNotContain(key, hit.ToString());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static void AssertScanned(ScanResult result)
    {
        Assert.True(result.Scanned.Count >= FileFloor,
            $"NoKeysTests scanned {result.Scanned.Count} files (floor {FileFloor}); the guard would pass on nothing.");

        foreach (var extension in new[] { ".cs", ".csproj", ".md" })
        {
            Assert.Contains(result.Scanned, file => file.EndsWith(extension, StringComparison.Ordinal));
        }

        foreach (var doc in new[]
        {
            "CONSTITUTION-P13-PLAN.md",
            "CONSTITUTION-P13-DESIGN-NOTES.md",
            "CONSTITUTION-P13-PROGRESS.md",
            "CONSTITUTION-P13-TECHNICAL-FINDING.md",
            "spikes/nyt-recipes.md",
        })
        {
            Assert.Contains(doc, result.Scanned);
        }
    }
}

/// <summary>
/// Shapes of real credentials, found by line. The patterns are built so that a test name, a
/// namespace, a path or an identifier cannot match them; each is exercised both ways above.
/// </summary>
internal static class KeyShapes
{
    private const string TokenChars = @"A-Za-z0-9+/=_\-";

    private static readonly (string Kind, Regex Pattern)[] Shapes =
    [
        ("Google API key", new(@"AIza[0-9A-Za-z_\-]{35}")),
        ("secret-key token", new(@"\bsk-[A-Za-z0-9_\-]{20,}")),
        ("GitHub token", new(@"\bgh[pousr]_[A-Za-z0-9]{36,}|\bgithub_pat_[A-Za-z0-9_]{22,}")),
        ("AWS access key id", new(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")),
        ("Slack token", new(@"\bxox[abprs]-[A-Za-z0-9\-]{10,}")),
        ("private key block", new(@"-----BEGIN [A-Z0-9 ]+-----")),
        // A quoted run of 32+ hex digits: the shape of most REST API keys.
        ("quoted hex run", new(@"[""'][0-9A-Fa-f]{32,}[""']")),
        // Bare hex is only a key when a key word is on the line; otherwise it is a checksum or a commit id.
        ("hex value near a key word", new(
            @"(?i)\b(?:key|token|secret|password|passphrase|credential)s?\b[^\r\n]{0,60}?(?<![0-9A-Za-z])[0-9a-f]{32,}(?![0-9A-Za-z])")),
        // A quoted 40+ character token holding both letters and digits.
        ("quoted long token", new(
            @"[""'](?=[" + TokenChars + @"]*[0-9])(?=[" + TokenChars + @"]*[A-Za-z])[" + TokenChars + @"]{40,}[""']")),
        // Unquoted (prose, markdown): 32+ characters with upper, lower and a digit, not part of a path or dotted name.
        ("long token", new(
            @"(?<![A-Za-z0-9+/=_.\-])(?=[A-Za-z0-9_\-]*[0-9])(?=[A-Za-z0-9_\-]*[a-z])(?=[A-Za-z0-9_\-]*[A-Z])[A-Za-z0-9_\-]{32,}(?![A-Za-z0-9_\-])")),
    ];

    /// <summary>A literal assigned to a password/passphrase/secret/token/key name, in C#, JSON, YAML or markdown.</summary>
    private static readonly Regex SecretAssignment = new(
        @"(?i)\b[A-Za-z_]*(?:password|passphrase|secret|apikey|api_key|token|privatekey)[""']?\s*(?::|=>?)\s*@?(?:""(?<v>[^""\r\n]*)""|'(?<v>[^'\r\n]*)'|`(?<v>[^`\r\n]*)`)");

    /// <summary>
    /// A value that says it is invented, or is a reference rather than a value. This is the escape
    /// hatch for tests that need a stand-in passphrase: call it "invented ...", "sample ...",
    /// "example ..." and the guard leaves it alone. Anything else assigned to a secret-named member
    /// is reported, even when it is short or word-like.
    /// </summary>
    private static readonly Regex StandIn = new(
        @"(?i)^$|^\s*<|^\$\{|^\{\{|^\$\(|^%|invented|example|sample|placeholder|dummy|fake|redacted|xxxx|your[-_ ]");

    public static IReadOnlyList<Finding> Find(string text)
    {
        var findings = new List<Finding>();
        foreach (var (kind, pattern) in Shapes)
        {
            foreach (Match match in pattern.Matches(text))
            {
                findings.Add(new Finding(GuardScan.LineOf(text, match.Index), kind));
            }
        }

        foreach (Match match in SecretAssignment.Matches(text))
        {
            if (!StandIn.IsMatch(match.Groups["v"].Value))
            {
                findings.Add(new Finding(GuardScan.LineOf(text, match.Index), "secret assignment"));
            }
        }

        return findings.Distinct().ToList();
    }
}
