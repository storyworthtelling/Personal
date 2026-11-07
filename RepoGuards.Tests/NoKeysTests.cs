using LogLoopReader;

namespace RepoGuards.Tests;

/// <summary>
/// No key anywhere in the repository. It is public and a key pushed once stays retrievable by commit
/// hash, so keys live in the encrypted Drive vault and Actions secrets, never in code, config, fixtures,
/// workflows or docs. This was Constitution's guard (C-12.2.1, G23) and Pulp's; it now reads every file
/// (privacy audit §3), with Constitution keeping its stricter rule for secret-named values.
///
/// <para>It reports file and line, never the matched text, so a failing run on a real key does not become
/// a second place the key is published. It is non-vacuous: the scan must cover a floor of files including
/// named docs, every detector shape must fire on an in-memory sample, and a planted bad file must come
/// back named by file and line.</para>
///
/// <para><b>What it cannot catch:</b> a key with no recognisable shape (a short or word-like secret, a
/// passphrase in prose), a bare 40-hex run (indistinguishable from a commit id) unless a key word is on the
/// same line, and a secret split across lines. Those are review's job.</para>
/// </summary>
public class NoKeysTests
{
    private const int FileFloor = 1000;

    private const char SingleQuote = '\'';
    private const char Backtick = '`';

    /// <summary>
    /// Files whose key shapes are published test vectors or canned replies, never credentials. Each says
    /// why; an entry nothing matches any more fails <see cref="EveryAllowanceIsStillNeeded"/>, and a new
    /// one is a decision for review rather than a diff.
    /// </summary>
    private static readonly (string File, string Why)[] Allowed =
    [
        ("FocusPhone/Codex.Tests/Music/TidalSignInTests.cs", "RFC 7636 appendix B's PKCE verifier and challenge"),
        ("FocusPhone/Codex.Tests/Music/TidalCatalogClientTests.cs", "a search-result id in a canned TIDAL reply"),
        ("Storyworthtelling.Diagnostics.Tests/LogKeyDeriveTests.cs", "PBKDF2 outputs for an invented passphrase, checked against Python"),
    ];

    [Fact]
    public void NoKeyShapedLiteralInTheRepository()
    {
        var result = Scan();
        AssertScanned(result);

        var hits = result.Hits.Where(hit => !IsAllowed(hit)).ToList();

        // The match itself is never printed: file, line and the kind of shape only.
        Assert.True(hits.Count == 0, "A key-shaped string is in the repository; keys belong in the vault:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void EveryAllowanceIsStillNeeded()
    {
        var hits = Scan().Hits;

        foreach (var (file, why) in Allowed)
        {
            Assert.True(hits.Any(hit => hit.File == file), $"{file} ({why}) no longer needs its allowance; remove it.");
        }
    }

    [Fact]
    public void ThisGuardIsScannedLikeEverythingElse()
    {
        var result = GuardScan.Run(GuardScan.RepoRoot(), (path, text) => []);
        Assert.Contains("RepoGuards.Tests/NoKeysTests.cs", result.Scanned);
        Assert.Contains("Tools/LogLoopReader/RepoShapes.cs", result.Scanned);
        Assert.Contains(".github/workflows/log-loop-guard.yml", result.Scanned);
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
        yield return ["secret assignment", "\"Refresh" + "Token\": \"" + "1//0gAbC" + "dEfGh7IjKl\""];
        yield return ["secret assignment", "api" + "key = " + Backtick + "Q7xZp2Lm9RtW4vKs" + Backtick];
    }

    /// <summary>The detector itself, against invented samples assembled at run time.</summary>
    [Theory]
    [MemberData(nameof(KeySamples))]
    public void TheDetectorRecognisesAKeyShape(string kind, string sample)
    {
        var findings = KeyShapes.Find("first line\n" + sample + "\nlast line");
        Assert.Contains(findings, finding => finding.Kind == kind && finding.Line == 2);
    }

    public static IEnumerable<object[]> StrictSamples()
    {
        yield return ["var " + "Pass" + "word = \"" + "hunter" + "2\";"];
        yield return ["Pass" + "phrase = \"" + "maple river tin" + "\""];
        yield return ["\"Refresh" + "Token\": \"" + "abc" + "123\""];
        yield return [string.Concat("client_", "secret", ": ", SingleQuote, "s3cr3t", SingleQuote)];
        yield return ["api" + "key = " + Backtick + "zzzz" + Backtick];
    }

    /// <summary>Constitution's strict rule: any secret-named value that does not call itself invented.</summary>
    [Theory]
    [MemberData(nameof(StrictSamples))]
    public void TheStrictRuleReportsAWordLikeSecret(string sample)
    {
        Assert.Contains(KeyShapes.Find(sample, strict: true), finding => finding.Kind == "secret assignment");
        Assert.DoesNotContain(KeyShapes.Find(sample), finding => finding.Kind == "secret assignment");
    }

    public static IEnumerable<object[]> OrdinarySamples()
    {
        yield return ["public void TheDetectorRecognisesAQuotedHexKeyInSourceFiles()"];
        yield return ["public void Clock12IsLowercaseWithoutLeadingZero(int hour, int minute, string expected)"];
        yield return ["public async Task RG18_APendingTransactionAndItsPostedSuccessorAreOneRow_KeepingTheOwnersCategory()"];
        yield return ["[Migration(\"20260913195622_P10ShelvingSeriesNameCoversAndWordCount\")]"];
        yield return ["private const string Fp1 = \"" + new string('1', 64) + "\";"];
        yield return ["Convert.FromBase64String(\"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH\")"];
        yield return ["\"com.example.sample.WeeklyRefreshWorker\""];
        yield return ["<PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.12.0\" />"];
        yield return ["`Tools/IconStudio/Tokens.cs` and `Storyworthtelling.Paper/AppIdentityTokens.cs`"];
        yield return ["the header rule is `#476F83` on the muted ink"];
        yield return ["ApiKey = \"test-key\", AccessToken = \"token-redwood\", RefreshToken = \"rt\""];
        yield return ["Pass" + "phrase = \"correct horse battery staple\""];
        yield return ["Pass" + "phrase = \"invented sample passphrase\""];
        yield return ["token: \"${{ secrets.GITHUB_TOKEN }}\""];
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
            GuardScan.Plant(root, "graphify-out/graph.json", "\"" + key + "\"");

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

    private static ScanResult Scan() =>
        GuardScan.Run(GuardScan.RepoRoot(), (path, text) => KeyShapes.Find(text, strict: path.StartsWith("Constitution/", StringComparison.Ordinal)));

    private static bool IsAllowed(Hit hit) =>
        Allowed.Any(allowed => allowed.File == hit.File);

    private static void AssertScanned(ScanResult result)
    {
        Assert.True(result.Scanned.Count >= FileFloor,
            $"NoKeysTests scanned {result.Scanned.Count} files (floor {FileFloor}); the guard would pass on nothing.");

        foreach (var file in RepoDocs.Named)
        {
            Assert.Contains(file, result.Scanned);
        }
    }
}

/// <summary>Files both guards must reach, so a broken walk cannot pass by reading nothing that matters.</summary>
internal static class RepoDocs
{
    public static readonly string[] Named =
    [
        "CLAUDE.md",
        "DESIGN.md",
        "LOG-LOOP-PLAN.md",
        ".github/workflows/generate-keystore.yml",
        "Constitution/CONSTITUTION-P13-PLAN.md",
        "Constitution/CONSTITUTION-P13-DESIGN-NOTES.md",
        "Constitution/spikes/nyt-recipes.md",
        "Ledger/design/index-design-book.html",
        "Foreword/Foreword.Tests/Maps/HomeLookupTests.cs",
        "FocusPhone/Codex.Tests/Plate/BusinessCardTests.cs",
    ];
}
