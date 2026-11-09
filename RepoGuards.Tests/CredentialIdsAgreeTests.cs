using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// A credential shared between apps has one id (audit 01 §15): <c>Storyworthtelling.Voice.Health.SharedCredentialIds</c>.
/// The Domain projects cannot reference that library, so each keeps the constant it uses; this holds every
/// literal copy to the shared value, so a health report written under one word is read back under it. And
/// there is one credential health store (audit 01 §14).
/// </summary>
public partial class CredentialIdsAgreeTests
{
    private static readonly Dictionary<string, string> Shared = new(StringComparer.Ordinal)
    {
        ["Claude"] = "claude",
        ["Gemini"] = "gemini",
        ["Remarkable"] = "remarkable",
        ["ReMarkable"] = "remarkable",
        ["GitHubUpdateToken"] = "github-update-token",
    };

    [Fact]
    public void EveryCopyOfASharedIdHasTheSharedValue()
    {
        var copies = IdFiles().SelectMany(file => SharedLiteral().Matches(File.ReadAllText(file))
                .Select(match => (File: Relative(file), Name: match.Groups["name"].Value, Value: match.Groups["value"].Value)))
            .ToList();

        Assert.NotEmpty(copies);
        var wrong = copies.Where(copy => Shared[copy.Name] != copy.Value).Select(copy => $"{copy.File}: {copy.Name} = \"{copy.Value}\"").ToList();
        Assert.True(wrong.Count == 0, "A shared credential id with another value:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void TheSharedLibraryHoldsTheSameValues()
    {
        var source = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), "Storyworthtelling.Voice", "Health", "CredentialHealthStore.cs"));
        foreach (var (name, value) in Shared.Where(pair => pair.Key != "ReMarkable"))
        {
            Assert.Contains($"public const string {name} = \"{value}\";", source);
        }
    }

    [Fact]
    public void ThereIsOneCredentialHealthStore()
    {
        var stores = Sources().Where(file => Regex.IsMatch(File.ReadAllText(file), @"\bclass CredentialHealthStore\b")).Select(Relative).ToList();
        Assert.Equal(["Storyworthtelling.Voice/Health/CredentialHealthStore.cs"], stores);
    }

    private static IEnumerable<string> IdFiles() =>
        Sources().Where(file => Path.GetFileName(file) is "CredentialIds.cs" or "ConnectionId.cs" or "RemarkableCredentialId.cs");

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');

    [GeneratedRegex(@"const string (?<name>Claude|Gemini|Remarkable|ReMarkable|GitHubUpdateToken) = ""(?<value>[^""]*)""")]
    private static partial Regex SharedLiteral();
}
