using System.Text.RegularExpressions;
using Xunit;

namespace Foreword.Tests.Guard;

/// <summary>
/// Every screen carries the binder shell: the in-page header with its area's rule, and the strip.
///
/// <para>This is the guard the shipped app needed. The kit's <c>BinderTabs</c> was built in
/// Release B and then wired to <b>no page at all</b>; every screen drew a centred Display label and
/// nothing under it, and the four areas DESIGN.md §9 gives the app were two quiet buttons in a
/// bottom bar — which left Calendar three taps deep inside Settings. Nothing failed. The app simply
/// did not look like the thing that was drawn, and no test could tell.</para>
///
/// <para>So the shell is a base class rather than a habit, and this says so: a new screen that
/// forgets it fails here rather than shipping.</para>
/// </summary>
public class BinderShellTests
{
    private static readonly Regex RootElement = new(@"<(?<name>[A-Za-z:.]+)\s", RegexOptions.Compiled);

    [Fact]
    public void EveryPageIsABinderPage()
    {
        var wrong = new List<string>();

        foreach (var xaml in Pages("*.xaml"))
        {
            var source = File.ReadAllText(xaml);
            var root = RootElement.Match(source[source.IndexOf("?>", StringComparison.Ordinal)..]);

            if (root.Groups["name"].Value != "pages:BinderPage")
            {
                wrong.Add($"{Path.GetFileName(xaml)} is a {root.Groups["name"].Value}, not a BinderPage — "
                          + "it will have no header rule and no binder strip.");
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

            if (!source.Contains(": BinderPage", StringComparison.Ordinal))
            {
                wrong.Add($"{name} does not derive from BinderPage.");
            }
            else if (!source.Contains("Wrap(ForewordArea.", StringComparison.Ordinal))
            {
                wrong.Add($"{name} never calls Wrap, so the frame is never put around its content.");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>
    /// The screen's name belongs to the header and nowhere else. A Display label left in the
    /// content prints the title twice — once under the rule, once above the first row — which is
    /// what every one of these pages did before the shell existed.
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
    /// Each of the four areas has exactly one root, and the strip navigates to it. A second page
    /// claiming to be an area's root is how a tab starts landing somewhere unexpected.
    /// </summary>
    [Fact]
    public void TheStripNavigatesToOneRootPerArea()
    {
        var shell = File.ReadAllText(Path.Combine(Root(), "Foreword.Android", "Pages", "BinderPage.cs"));

        foreach (var page in new[] { "TasksPage", "CalendarsPage", "SetupPage" })
        {
            Assert.Contains($"new {page}()", shell, StringComparison.Ordinal);
        }

        // Today is the navigation root, so turning to it is a pop rather than a push.
        Assert.Contains("PopToRootAsync", shell, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Pages(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root(), "Foreword.Android", "Pages"), pattern)
            .Where(file => !Path.GetFileName(file).StartsWith("BinderPage", StringComparison.Ordinal))
            // *.xaml.cs also matches *.xaml when the pattern is *.xaml, so filter the pairing here.
            .Where(file => pattern != "*.xaml" || !file.EndsWith(".cs", StringComparison.Ordinal));

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Foreword.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
