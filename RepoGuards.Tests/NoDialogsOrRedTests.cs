using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// DESIGN.md §7–§8 (audit W8.2, reports 02 §1, §6, §12 and 04 §3, §26): success is an inline state
/// change, a problem waits behind the dog-ear, a choice or a few words are asked for on a paper card,
/// and a destructive act happens at once with an undo — so there is no Destructive button, no red on
/// a long-press card, no toast and no platform dialog.
///
/// <para>The native heads moved in W8.2a; the MAUI heads' <c>Display*</c> calls moved in W8.2b onto the
/// kit's <c>PaperCards</c>, inline lines and the dog-ear.</para>
/// </summary>
public partial class NoDialogsOrRedTests
{
    [Fact]
    public void NoScreenAsksForADestructiveButton()
    {
        var found = Hits(AllUi(), DestructiveButton());
        Assert.True(found.Count == 0, "§7 has no Destructive button; use a secondary or quiet one and an undo:\n" + string.Join("\n", found));
    }

    [Fact]
    public void NoLongPressCardMarksAnActionDestructive()
    {
        var found = Hits(Native(), DestructiveCardAction());
        Assert.True(found.Count == 0, "A card action is undone from the corner, not painted red:\n" + string.Join("\n", found));
    }

    [Fact]
    public void NoNativeHeadShowsAToast()
    {
        var found = Hits(Native(), Toast());
        Assert.True(found.Count == 0, "Say it inline or behind the dog-ear, not in a toast:\n" + string.Join("\n", found));
    }

    [Fact]
    public void NoNativeHeadOpensAPlatformDialog()
    {
        var found = Hits(Native(), PlatformDialog());
        Assert.True(found.Count == 0, "Ask on a paper card (ActionCard), not an AlertDialog:\n" + string.Join("\n", found));
    }

    [Fact]
    public void NoMauiHeadOpensAPlatformDialog()
    {
        var found = Hits(Maui(), MauiDialog());
        Assert.True(found.Count == 0, "Ask on a paper card (PaperCards), or say it inline, not in a platform alert:\n" + string.Join("\n", found));
    }

    [GeneratedRegex(@"\bDisplay(?:Alert|ActionSheet|PromptAsync)(?:Async)?\(")]
    private static partial Regex MauiDialog();

    [GeneratedRegex(@"DestructiveButton")]
    private static partial Regex DestructiveButton();

    [GeneratedRegex(@"\bDestructive\s*[:=]|bool Destructive\b|\.Destructive\b")]
    private static partial Regex DestructiveCardAction();

    [GeneratedRegex(@"\bToast\.MakeText\(")]
    private static partial Regex Toast();

    [GeneratedRegex(@"\bAlertDialog\.Builder\b|new AlertDialog\b|\bDatePickerDialog\b|\bTimePickerDialog\b")]
    private static partial Regex PlatformDialog();

    private static List<string> Hits(IEnumerable<string> files, Regex pattern) =>
        files.SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(hit => !hit.line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                && !hit.line.TrimStart().StartsWith("///", StringComparison.Ordinal)
                && pattern.IsMatch(hit.line))
            .Select(hit => $"{Relative(hit.file)}:{hit.index + 1}")
            .ToList();

    private static IEnumerable<string> Native() =>
        Sources("FocusPhone", "Pulp", "Constitution");

    private static IEnumerable<string> Maui() =>
        Sources("Ledger", "Storyboarder", "Foreword", "Storyworthtelling.Maui");

    private static IEnumerable<string> AllUi() =>
        Sources("FocusPhone", "Pulp", "Constitution", "Ledger", "Storyboarder", "Foreword", "Storyworthtelling.Maui");

    private static IEnumerable<string> Sources(params string[] folders) =>
        folders.SelectMany(folder => Directory.EnumerateFiles(
                Path.Combine(GuardScan.RepoRoot(), folder), "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part.EndsWith(".Tests", StringComparison.Ordinal)));

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');
}
