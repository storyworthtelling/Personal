using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// Foreword's and Index's tablet pages are the tablet's own page (audit 01 §12): drawn on their Letter and A4 boards
/// and placed on <c>Storyworthtelling.Paper.RemarkablePage</c> through a <c>TabletSheet</c>, so the read-back is told
/// the ink's place on the page it was handed. A page begun at a width and height of its own goes back to the tablet
/// shrinking and letterboxing it, and to a prompt whose numbers are on a different page from the PDF.
/// </summary>
public class TabletPagesAreTheTabletsTests
{
    [Fact]
    public void ForewordAndIndexBeginEveryPageOnASheet()
    {
        var found = Sources("Foreword/Foreword.Infrastructure", "Ledger/Ledger.Infrastructure")
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(hit => Regex.IsMatch(hit.line, @"\.BeginPage\([^()]*,"))
            .Select(hit => $"{Relative(hit.file)}:{hit.index + 1}")
            .ToList();

        Assert.True(found.Count == 0, "Begin the page with a TabletSheet (ForewordSheet, IndexSheet):\n" + string.Join("\n", found));
    }

    [Fact]
    public void NoPromptNamesTheOldPageSizes()
    {
        // The read-back prompts state the page's size; Letter's and A4's numbers in one are a prompt about another page.
        var found = Sources("Foreword/Foreword.Infrastructure", "Ledger/Ledger.Infrastructure")
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\b(612|792|595|842)(\.0)?f?\b"))
            .Where(file => !file.EndsWith("ForewordSheet.cs", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        Assert.True(found.Count == 0, "Take the page's size from its TabletSheet:\n" + string.Join("\n", found));
    }

    private static IEnumerable<string> Sources(params string[] folders) =>
        folders.SelectMany(folder => Directory.EnumerateFiles(Path.Combine(GuardScan.RepoRoot(), folder), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');
}
