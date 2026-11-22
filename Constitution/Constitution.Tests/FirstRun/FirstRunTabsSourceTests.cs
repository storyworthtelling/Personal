namespace Constitution.Tests.FirstRun;

/// <summary>
/// While the Welcome page shows, the binder tabs cannot be taken: a tap would set the area and redraw the same
/// Welcome page, so a tab that answers and goes nowhere reads as broken. The tabs are disabled (Enabled = false,
/// so TalkBack says so) while first run shows, and enabled again once anything else is drawn. The head is
/// compile-checked, not run here, so this reads the source.
/// </summary>
public class FirstRunTabsSourceTests
{
    /// <summary>The body of the named method: from its signature to its matching closing brace.</summary>
    internal static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "Not found: " + signature);
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException("Unbalanced braces after " + signature);
    }

    private static string Shell() =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "ConstitutionActivity.cs"));

    [Fact]
    public void TheWelcomePageDisablesTheTabs()
    {
        Assert.Contains("binder.SetTabsEnabled(false)", Body(Shell(), "private void RenderFirstRun()"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnythingButTheWelcomePageEnablesThemAgain()
    {
        var render = Body(Shell(), "private void RenderPage()");
        var afterFirstRun = render[render.IndexOf("RenderFirstRun();", StringComparison.Ordinal)..];

        Assert.Contains("binder.SetTabsEnabled(true)", afterFirstRun, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStripDisablesEachTabAndKeepsItAcrossARebuild()
    {
        var tabs = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "..", "FocusPhone", "Codex.Ui.Android", "BinderTabs.cs"));

        Assert.Contains("Enabled = tabsEnabled", Body(tabs, "public void SetTabsEnabled(bool enabled)"), StringComparison.Ordinal);
        Assert.Contains("Enabled = tabsEnabled", Body(tabs, "private View BuildTab("), StringComparison.Ordinal);
    }
}
