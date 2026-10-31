using System.Text.RegularExpressions;
using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// The role extra Constitution puts on its notifications and Flyleaf reads (P13 C-10.2.1, PC1).
/// The values are typed out here on purpose; Constitution's <c>NoticeNotifications.cs</c> is read by
/// content, so the two halves can only agree by both agreeing with this file. Nothing fails at run
/// time if they drift: the ribbon quietly goes back to the importance mapping.
/// </summary>
public class NoticeRoleContractTests
{
    [Fact]
    public void TheWireNamesAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.constitution.NOTICE_ROLE", NotificationBookmark.RoleExtraKey);
        Assert.Equal("problem", NotificationBookmark.ProblemWire);
        Assert.Equal("reference", NotificationBookmark.ReferenceWire);
    }

    [Fact]
    public void ConstitutionSendsTheSameThree()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "Constitution", "Constitution.Domain", "Notices", "NoticeNotifications.cs"));
        Assert.Equal(NotificationBookmark.RoleExtraKey, Literal(source, "RoleExtraKey"));
        Assert.Equal(NotificationBookmark.ProblemWire, Literal(source, "ProblemRole"));
        Assert.Equal(NotificationBookmark.ReferenceWire, Literal(source, "ReferenceRole"));
    }

    [Fact]
    public void FlyleafReadsTheExtraThroughTheContractAndBroadcastsIt()
    {
        var listener = File.ReadAllText(Path.Combine(RepoRoot(), "FocusPhone", "Flyleaf.Android", "TileNotificationListener.cs"));
        Assert.Contains("NotificationBookmark.RoleExtraKey", listener, StringComparison.Ordinal);
        Assert.Contains("NotificationBookmark.Strongest(", listener, StringComparison.Ordinal);
        Assert.Contains("ExtraRoles", listener, StringComparison.Ordinal);

        var home = File.ReadAllText(Path.Combine(RepoRoot(), "FocusPhone", "Flyleaf.Android", "HomeActivity.cs"));
        Assert.Contains("NotificationBookmark.Decide(", home, StringComparison.Ordinal);
        Assert.Contains("TileNotificationListener.ExtraRoles", home, StringComparison.Ordinal);
    }

    [Fact]
    public void NoNumberCrossesTheBookmark()
    {
        // The ribbon is never a count (C-10.2.2): the listener's role array is strings, and nothing it
        // broadcasts is derived from how many notifications there are.
        var listener = File.ReadAllText(Path.Combine(RepoRoot(), "FocusPhone", "Flyleaf.Android", "TileNotificationListener.cs"));
        Assert.DoesNotContain(".Count()", listener, StringComparison.Ordinal);
        Assert.DoesNotContain("PutExtra(\"count\"", listener, StringComparison.Ordinal);
    }

    private static string Literal(string source, string name)
    {
        var match = Regex.Match(source, @"public const string " + name + @" = ""([^""]*)"";");
        Assert.True(match.Success, name + " was not found as a literal.");
        return match.Groups[1].Value;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
