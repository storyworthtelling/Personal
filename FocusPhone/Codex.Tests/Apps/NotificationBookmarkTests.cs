using Codex.Domain.Apps;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Apps;

/// <summary>
/// C-10.2.1 — the ribbon reads the role (P13 plan, D91, PC1, PC2). A Constitution notification that
/// carries a role extra wins over the importance mapping; every other notification keeps today's
/// mapping, so no other app's bookmark changes.
/// </summary>
public class NotificationBookmarkTests
{
    private const string Constitution = PolicyContext.ConstitutionPackage;
    private const string Other = "com.example.mail";

    [Fact]
    public void RoleExtraWins()
    {
        // A low-urgency notification that says problem is rose; a high-urgency one that says reference is slate.
        Assert.Equal(BookmarkRole.Problem, NotificationBookmark.Decide(Constitution, 0, "problem"));
        Assert.Equal(BookmarkRole.Reference, NotificationBookmark.Decide(Constitution, 2, "reference"));
    }

    [Theory]
    [InlineData(2, BookmarkRole.Problem)]
    [InlineData(1, BookmarkRole.Progress)]
    [InlineData(0, BookmarkRole.Resolved)]
    public void NoRoleKeepsTheImportanceMapping(int urgency, BookmarkRole expected)
    {
        Assert.Equal(expected, NotificationBookmark.Decide(Constitution, urgency, null));
        Assert.Equal(expected, NotificationBookmark.Decide(Constitution, urgency, ""));
        Assert.Equal(expected, NotificationBookmark.Decide(Other, urgency, null));
    }

    [Fact]
    public void AnUnknownRoleKeepsTheImportanceMapping()
    {
        Assert.Equal(BookmarkRole.Progress, NotificationBookmark.Decide(Constitution, 1, "action"));
        Assert.Equal(BookmarkRole.Progress, NotificationBookmark.Decide(Constitution, 1, "Problem "));
    }

    [Fact]
    public void OnlyConstitutionsRoleIsTrusted()
    {
        // Any app can put an extra on its own notification; only the suite book's is believed.
        Assert.Equal(BookmarkRole.Progress, NotificationBookmark.Decide(Other, 1, "problem"));
        Assert.Equal(BookmarkRole.Resolved, NotificationBookmark.Decide(Other, 0, "reference"));
    }

    [Fact]
    public void TheStrongestRoleOfSeveralWins()
    {
        Assert.Equal("problem", NotificationBookmark.Strongest(Constitution, ["reference", null, "problem"]));
        Assert.Equal("reference", NotificationBookmark.Strongest(Constitution, [null, "reference", ""]));
        Assert.Null(NotificationBookmark.Strongest(Constitution, [null, "", "action"]));
        Assert.Null(NotificationBookmark.Strongest(Constitution, []));
        Assert.Null(NotificationBookmark.Strongest(Other, ["problem"]));
    }
}
