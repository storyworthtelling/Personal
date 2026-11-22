using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>A correspondence's row title, for the Mailbox and the watch alike (P14 B1).</summary>
public class ThreadTitlesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 16, 20, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string> Book = new()
    {
        ["2125550101"] = "Lee",
        ["2125550102"] = "Robin",
        ["2125550103"] = "Sam",
        ["2125550104"] = "Juniper",
    };

    private static string NameOf(string key) => Book.GetValueOrDefault(key, key);

    private static ContactThread Group(params string[] members) =>
        new(TimelineMerger.GroupThreadKey("mms:17"), [], T0) { IsGroup = true, Participants = members };

    [Fact]
    public void APersonIsTheirName()
    {
        Assert.Equal("Lee", ThreadTitles.ForRow(new ContactThread("2125550101", [], T0), GroupNames.Empty, NameOf));
    }

    [Fact]
    public void AGroupIsItsOwnName()
    {
        var key = TimelineMerger.GroupThreadKey("mms:17");
        var named = GroupNames.Empty.Learn(key, "Team snacks");

        Assert.Equal("Team snacks", ThreadTitles.ForRow(Group("2125550101", "2125550102"), named, NameOf));
        Assert.Equal("Snacks", ThreadTitles.ForRow(Group("2125550101"), named.Name(key, "Snacks"), NameOf));
    }

    [Fact]
    public void AnUnnamedGroupIsThreeNamesAndHowManyMore()
    {
        Assert.Equal("Lee, Robin", ThreadTitles.ForRow(Group("2125550101", "2125550102"), GroupNames.Empty, NameOf));
        Assert.Equal("Lee, Robin, Sam +1", ThreadTitles.ForRow(Group("2125550101", "2125550102", "2125550103", "2125550104"), GroupNames.Empty, NameOf));
    }

    [Fact]
    public void AGroupWithNobodyKnownIsGroup()
    {
        Assert.Equal("Group", ThreadTitles.ForRow(Group(), GroupNames.Empty, NameOf));
    }
}
