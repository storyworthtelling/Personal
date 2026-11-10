using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// <c>Storyworthtelling.Paper.IPaperDelivery</c> is the only door to the tablet for an upload (audit 01 §11). Only Folio
/// and Plate used it: Foreword, Index and Constitution uploaded straight to the client, so the size cap and the
/// out-of-memory refusal that kept Folio's 173 MB report from being tried six times existed in Folio alone, and the
/// client was built three different ways, one with a snapshot of the pairing and one with no health report.
/// </summary>
public class OneRemarkableDoorTests
{
    private const string Factory = "Storyworthtelling.Remarkable/RemarkableConnection.cs";

    [Theory]
    [InlineData(@"new RemarkableStorageClient\(")]
    [InlineData(@"new RemarkableAuthService\(")]
    [InlineData(@"AddHttpClient<IRemarkable(Client|AuthProvider)")]
    public void OnlyTheFactoryBuildsTheClient(string pattern)
    {
        var found = Sources()
            .Where(file => Relative(file) != Factory && Regex.IsMatch(File.ReadAllText(file), pattern))
            .Select(Relative)
            .ToList();

        Assert.True(found.Count == 0, $"Build the tablet client with RemarkableConnection.Create ({pattern}):\n" + string.Join("\n", found));
    }

    [Fact]
    public void NoAppUploadsPastTheDeliverySeam()
    {
        // Reading, listing and trashing through the client stay; sending a document goes through IPaperDelivery.
        var found = Sources()
            .Where(file => !Relative(file).StartsWith("Storyworthtelling.Paper/", StringComparison.Ordinal)
                && !Relative(file).StartsWith("Storyworthtelling.Remarkable/", StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\bIRemarkableClient\b|RemarkableConnection\."))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(hit => Regex.IsMatch(hit.line, @"\.(Upload|EnsureFolder)\("))
            .Select(hit => $"{Relative(hit.file)}:{hit.index + 1}")
            .ToList();

        Assert.True(found.Count == 0, "Send a document through IPaperDelivery:\n" + string.Join("\n", found));
    }

    [Fact]
    public void EveryHeadGivesTheTabletTheSharedTimeout()
    {
        // The 100-second default cut off large documents in Foreword and Index while Folio waited five minutes.
        var found = Sources()
            .Where(file => Relative(file) != Factory)
            .Where(file => File.ReadAllText(file).Contains("RemarkableConnection.Create(", StringComparison.Ordinal))
            .Where(file => !File.ReadAllText(file).Contains("RemarkableLimits.Timeout", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        Assert.True(found.Count == 0, "Give the tablet's HttpClient RemarkableLimits.Timeout:\n" + string.Join("\n", found));
    }

    [Fact]
    public void TheSizeLimitLivesOnce()
    {
        // 100_000_000 is also a version-code band and a timestamp scale, so only a file about the tablet counts.
        var found = Sources()
            .Select(file => (file, text: File.ReadAllText(file)))
            .Where(f => f.text.Contains("RemarkableMaxBytes", StringComparison.Ordinal)
                || (f.text.Contains("100_000_000", StringComparison.Ordinal)
                    && f.text.Contains("emarkable", StringComparison.Ordinal)))
            .Select(f => f.file)
            .Select(Relative)
            .Where(file => file != "Storyworthtelling.Remarkable/RemarkableLimits.cs")
            .ToList();

        Assert.True(found.Count == 0, "Use RemarkableLimits.MaxBytes:\n" + string.Join("\n", found));
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');
}
