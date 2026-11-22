using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// Success is an inline state change (DESIGN.md §7–§8): a Copy button says "Copied". The shared log-loop
/// diagnostics screen copied in silence, so a press read as nothing happening (and was judged a dead tap).
/// Every head or kit file that makes a button labelled "Copy" (a native button factory, or a XAML button and
/// its code-behind) must also set "Copied". A paper card's "Copy" choice is not a button and is not read.
/// </summary>
public partial class CopySaysCopiedTests
{
    [Fact]
    public void EveryCopyButtonSaysCopied()
    {
        var silent = new List<string>();
        foreach (var file in Sources())
        {
            var text = File.ReadAllText(file);
            if (file.EndsWith(".xaml", StringComparison.Ordinal))
            {
                if (!XamlCopyButton().IsMatch(text))
                {
                    continue;
                }
                var codeBehind = file + ".cs";
                if (!File.Exists(codeBehind) || !File.ReadAllText(codeBehind).Contains("\"Copied\"", StringComparison.Ordinal))
                {
                    silent.Add(Relative(file));
                }
            }
            else if (NativeCopyButton().IsMatch(text) && !text.Contains("\"Copied\"", StringComparison.Ordinal))
            {
                silent.Add(Relative(file));
            }
        }

        Assert.True(silent.Count == 0, "A Copy button should say \"Copied\" once it has copied:\n" + string.Join("\n", silent));
    }

    [Fact]
    public void TheGuardFindsTheButtonsItHolds()
    {
        var found = Sources().Count(file => File.ReadAllText(file) is var text
            && (NativeCopyButton().IsMatch(text) || XamlCopyButton().IsMatch(text)));
        Assert.True(found >= 4, $"Only {found} Copy buttons were found; the patterns have drifted from the code.");
    }

    [GeneratedRegex(@"Button\(\s*\w+\s*,\s*""Copy""")]
    private static partial Regex NativeCopyButton();

    [GeneratedRegex(@"<Button\b[^>]*\bText=""Copy""")]
    private static partial Regex XamlCopyButton();

    private static IEnumerable<string> Sources() =>
        new[] { "FocusPhone", "Pulp", "Constitution", "Ledger", "Storyboarder", "Foreword", "Storyworthtelling.Maui", "Watch" }
            .Select(folder => Path.Combine(GuardScan.RepoRoot(), folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part.EndsWith(".Tests", StringComparison.Ordinal)));

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');
}
