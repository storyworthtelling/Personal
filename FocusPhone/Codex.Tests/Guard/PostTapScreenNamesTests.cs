using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// A tap line names the screen it happened on, and that line is uploaded (log loop, C10). Post's areas change
/// their in-page title to the open correspondence — a person's name or address — so a screen named from
/// <c>Title</c> put that person into every tap verdict on the thread. The screen is the area's tab label,
/// which never changes.
/// </summary>
public class PostTapScreenNamesTests
{
    private static readonly Regex ScreenCall = new(@"TapWatch\.Screen\((?<argument>[^;]*)\);");

    [Fact]
    public void NoTapScreenInPostIsNamedFromATitle()
    {
        var offenders = LogLoopSource.Sources("FocusPhone/Post.Android")
            .SelectMany(file => ScreenCall.Matches(file.Text)
                .Where(m => m.Groups["argument"].Value.Contains("Title", StringComparison.Ordinal))
                .Select(m => $"{file.Path}: {m.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Names the tap screen after a title: " + string.Join(", ", offenders));
    }

    [Fact]
    public void ThePostShellNamesTheScreenByItsTab()
    {
        Assert.Contains(
            "TapWatch.Screen(Active.Tab.Label)",
            LogLoopSource.File("FocusPhone/Post.Android/PostActivity.cs"),
            StringComparison.Ordinal);
    }
}
