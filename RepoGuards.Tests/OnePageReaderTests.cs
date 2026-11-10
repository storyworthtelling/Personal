using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// A written-on page goes to Claude through <c>Storyworthtelling.Handwriting.PageReader</c> (audit 01 §12). The shared
/// extractor, Foreword's journal transcriber and Constitution's ink read each built the request by hand, and
/// Constitution's checked a copy of the key taken at start-up.
/// </summary>
public class OnePageReaderTests
{
    private const string Reader = "Storyworthtelling.Handwriting/PageReader.cs";

    [Fact]
    public void OnlyThePageReaderSendsAPdfToClaude()
    {
        var found = Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/'))
            .Where(relative => relative != Reader && relative != "Storyworthtelling.Voice/Claude/ClaudeMessagesClient.cs")
            .Where(relative => Regex.IsMatch(File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), relative)), @"ClaudeBlock\.Pdf\("))
            .ToList();

        Assert.True(found.Count == 0, "Read a page with Storyworthtelling.Handwriting.PageReader:\n" + string.Join("\n", found));
    }
}
