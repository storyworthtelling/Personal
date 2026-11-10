using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// The shared kinds of line take their source from <c>Storyworthtelling.Diagnostics.LogSources</c> (audit 08 §31), so the
/// digest groups a vault refusal, a worker or a contract the same way in every app.
/// </summary>
public class LogSourcesAreSharedTests
{
    [Fact]
    public void NoHeadSpellsASharedSourceByHand()
    {
        var found = Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(hit => Regex.IsMatch(hit.line,
                @"\b(Warn|Info|Write|Trail|Quiet)\(\s*""(?i:worker|vault|permission|crash|exit|drive)""")
                || Regex.IsMatch(hit.line, @"\(\s*""\w+"",\s*ContractBroadcast\.(SendLine|ReceiptLine)\("))
            .Select(hit => $"{Path.GetRelativePath(GuardScan.RepoRoot(), hit.path)}:{hit.index + 1}")
            .ToList();

        Assert.True(found.Count == 0, "Use LogSources (Vault, Worker, Contract(name), …):\n" + string.Join("\n", found));
    }
}
