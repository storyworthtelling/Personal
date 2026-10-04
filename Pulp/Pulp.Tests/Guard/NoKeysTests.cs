using System.Text.RegularExpressions;

namespace Pulp.Tests.Guard;

/// <summary>
/// G7 — no provider key and no TMDB in source. Keys live in the encrypted Drive vault
/// (<c>SecretsVault.Pulp</c>) and Actions secrets; the repository is public and a key pushed once
/// stays retrievable by commit hash. TMDB is excluded by decision (its terms bar this use), so its
/// hosts must not appear at all.
/// </summary>
public class NoKeysTests
{
    /// <summary>Shapes of real credentials. Built so a test name or a namespace cannot match.</summary>
    private static readonly Regex[] KeyShapes =
    [
        // Google API keys.
        new(@"AIza[0-9A-Za-z_\-]{35}"),
        // Secret-key style tokens (sk-..., sk-ant-...).
        new(@"\bsk-[A-Za-z0-9_\-]{20,}"),
        // GitHub tokens.
        new(@"\bgh[pousr]_[A-Za-z0-9]{36,}"),
        // A quoted run of 32+ hex digits: the shape of most REST API keys (OMDb, RapidAPI, ...).
        new(@"""[0-9A-Fa-f]{32,}"""),
        // A quoted 40+ character base64/url-safe token holding both letters and digits.
        new(@"""(?=[A-Za-z0-9+/=_\-]*[0-9])(?=[A-Za-z0-9+/=_\-]*[A-Za-z])[A-Za-z0-9+/=_\-]{40,}"""),
    ];

    private static readonly Regex TmdbHost = new(@"themoviedb\.org|api\.tmdb\.org", RegexOptions.IgnoreCase);

    private static readonly string[] Scanned = [".cs", ".csproj", ".xml", ".json", ".yml", ".yaml", ".props", ".targets"];

    [Fact]
    public void NoKeyShapedLiteralInPulpSource()
    {
        var files = SourceFiles();

        var offenders = files
            .SelectMany(file => KeyShapes
                .Select(shape => shape.Match(File.ReadAllText(file)))
                .Where(match => match.Success)
                .Select(match => $"{PulpSource.Relative(file)}: a key-shaped literal ({match.Value.Length} chars)"))
            .ToList();

        // The match itself is never printed: if this fails on a real key, the test log must not
        // become a second place the key is published.
        Assert.True(offenders.Count == 0,
            "A key-shaped literal is in Pulp source; keys belong in the vault:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoTmdbHostInPulpSource()
    {
        var offenders = SourceFiles()
            .Where(file => TmdbHost.IsMatch(File.ReadAllText(file)))
            .Select(PulpSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "TMDB is named in Pulp source:\n" + string.Join("\n", offenders));
    }

    /// <summary>The detector itself, against invented keys assembled at run time so none sits in source.</summary>
    [Theory]
    [InlineData("AIza", 35, 'Q')]
    [InlineData("sk-", 40, 'q')]
    [InlineData("ghp_", 36, 'Z')]
    public void TheDetectorRecognisesAKeyShape(string prefix, int length, char filler)
    {
        var invented = "var key = \"" + prefix + new string(filler, length) + "\";";
        Assert.Contains(KeyShapes, shape => shape.IsMatch(invented));
    }

    [Fact]
    public void TheDetectorRecognisesAQuotedHexKey()
    {
        var invented = "\"" + string.Concat(Enumerable.Repeat("0a1b", 8)) + "\"";
        Assert.Contains(KeyShapes, shape => shape.IsMatch(invented));
    }

    [Theory]
    [InlineData("public void TheDetectorRecognisesAQuotedHexKeyInSourceFiles()")]
    [InlineData("\"com.storyworthtelling.pulp.WeeklyRefreshWorker\"")]
    [InlineData("\"storyworthtelling-pulp-household.json\"")]
    [InlineData("Version=\"17.12.0\"")]
    public void TheDetectorIgnoresOrdinarySource(string line)
    {
        Assert.DoesNotContain(KeyShapes, shape => shape.IsMatch(line));
    }

    private static IReadOnlyList<string> SourceFiles()
    {
        var files = PulpSource.Files(Scanned)
            .Where(file => !PulpSource.IsGuard(file, "NoKeysTests.cs"))
            .ToList();
        Assert.True(files.Count > 0, "NoKeysTests scanned no files; the guard would pass on nothing.");
        return files;
    }
}
