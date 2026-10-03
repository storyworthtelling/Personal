using System.Text.RegularExpressions;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Every screen in Ledger carries the binder shell: the in-page header with its area's rule, and
/// the strip (DESIGN.md §5, §7, §9).
///
/// <para>Modeled on Foreword's <c>BinderShellTests</c>/Folio's <c>ScreenFrameTests</c> — the guard
/// those heads needed after their own binder strip was built, tested, and wired to no page at all.
/// Before this, both of Ledger's screens navigated by a plain bottom-bar button ("Settings" on the
/// Balances screen) instead of the strip DESIGN.md §9 gives every app, which is the identical
/// failure. So the shell is a base class rather than a habit, and this says so: a new screen that
/// forgets it fails here rather than shipping.</para>
/// </summary>
public class LedgerBinderShellTests
{
    private static readonly Regex RootElement = new(@"<(?<name>[A-Za-z:.]+)\s", RegexOptions.Compiled);

    [Fact]
    public void EveryPageIsALedgerBinderPage()
    {
        var wrong = new List<string>();

        foreach (var xaml in Pages("*.xaml"))
        {
            var source = File.ReadAllText(xaml);
            var root = RootElement.Match(source[source.IndexOf("?>", StringComparison.Ordinal)..]);

            if (root.Groups["name"].Value != "pages:LedgerBinderPage")
            {
                wrong.Add($"{Path.GetFileName(xaml)} is a {root.Groups["name"].Value}, not a "
                    + "LedgerBinderPage — it will have no header rule and no binder strip.");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void EveryPageDeclaresWhichAreaItBelongsTo()
    {
        var wrong = new List<string>();

        foreach (var code in Pages("*.xaml.cs"))
        {
            var source = File.ReadAllText(code);
            var name = Path.GetFileName(code);

            if (!source.Contains(": LedgerBinderPage", StringComparison.Ordinal))
            {
                wrong.Add($"{name} does not derive from LedgerBinderPage.");
            }
            else if (!source.Contains("Wrap(LedgerArea.", StringComparison.Ordinal))
            {
                wrong.Add($"{name} never calls Wrap, so the frame is never put around its content.");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>
    /// The screen's name belongs to the header and nowhere else. A DisplayLabel left in the
    /// content prints the title twice — once under the rule, once above the first row.
    /// </summary>
    [Fact]
    public void NoPageDrawsItsOwnTitle()
    {
        var offenders = Pages("*.xaml")
            .Where(xaml => File.ReadAllText(xaml).Contains("DisplayLabel", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These draw a Display label of their own; the header already carries the screen's name: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// Navigating by a button to another area is the thing the binder replaced (the "Settings"
    /// button that used to sit in the Balances bottom bar). Where you can go is the strip; the bar
    /// carries what the screen is <i>for</i>.
    /// </summary>
    [Fact]
    public void NoScreenNavigatesByAButtonToAnArea()
    {
        var offenders = new List<string>();
        foreach (var code in Pages("*.xaml.cs").Concat(Pages("*.xaml")))
        {
            var source = File.ReadAllText(code);
            foreach (var area in new[] { "Balances", "Settings" })
            {
                if (Regex.IsMatch(source, $@"Text\s*=\s*""{area}""", RegexOptions.None)
                    && !Path.GetFileName(code).StartsWith("LedgerBinderPage", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(code)} names a \"{area}\" button — is it navigating "
                        + "to an area the binder strip already reaches?");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    private static IEnumerable<string> Pages(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root(), "Ledger.Android", "Pages"), pattern)
            .Where(file => !Path.GetFileName(file).StartsWith("LedgerBinderPage", StringComparison.Ordinal))
            // *.xaml.cs also matches *.xaml when the pattern is *.xaml, so filter the pairing here.
            .Where(file => pattern != "*.xaml" || !file.EndsWith(".cs", StringComparison.Ordinal));

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Ledger.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
