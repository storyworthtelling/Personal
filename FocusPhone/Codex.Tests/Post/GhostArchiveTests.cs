using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// The archive holds what filing keeps: a ghost of every message the owner dealt with,
/// after the original has been deleted at the source. It is a SEPARATE store from the live
/// cache rather than a flag on it, because the two answer to different masters — the cache
/// mirrors the source and is rebuilt from it, while the archive is the only remaining copy
/// and must never be rebuilt from anything.
/// </summary>
public class GhostArchiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    private static TimelineItem Message(string id, string who = "her@example.com", string? body = "Seven?") =>
        new(TimelineItemKind.Email, who, Now, body, Incoming: true, SourceId: id) { Subject = "Dinner" };

    [Fact]
    public void FilingAnItemPutsAGhostInTheArchive()
    {
        var archive = new GhostArchive();

        archive.File(Message("m1"), Now);

        Assert.Single(archive.All);
        Assert.Equal("m1", archive.All.Single().Item.SourceId);
    }

    // The filing date is not the message's date. "When did I deal with this" is a
    // different question from "when did it arrive", and Filed is ordered by the former.
    [Fact]
    public void AGhostRemembersWhenItWasFiledNotOnlyWhenItArrived()
    {
        var archive = new GhostArchive();
        var later = Now.AddDays(3);

        archive.File(Message("m1"), later);

        var ghost = archive.All.Single();
        Assert.Equal(later, ghost.FiledAtUtc);
        Assert.Equal(Now, ghost.Item.TimestampUtc);
    }

    [Fact]
    public void MostRecentlyFiledComesFirst()
    {
        var archive = new GhostArchive();
        archive.File(Message("old"), Now);
        archive.File(Message("new"), Now.AddHours(1));

        Assert.Equal(["new", "old"], archive.All.Select(ghost => ghost.Item.SourceId));
    }

    // Filing the same thing twice is one ghost. A retried sync must not double the archive.
    [Fact]
    public void FilingTheSameItemTwiceKeepsOneGhost()
    {
        var archive = new GhostArchive();
        archive.File(Message("m1"), Now);
        archive.File(Message("m1"), Now.AddHours(1));

        Assert.Single(archive.All);
    }

    // Un-filing is allowed at any time, and what comes back is the ghost — the source
    // original is already gone, so there is nothing else it could be.
    [Fact]
    public void UnfilingReturnsTheGhostAndRemovesIt()
    {
        var archive = new GhostArchive();
        archive.File(Message("m1"), Now);

        var returned = archive.Unfile("m1");

        Assert.NotNull(returned);
        Assert.Equal("m1", returned!.SourceId);
        Assert.Empty(archive.All);
    }

    [Fact]
    public void UnfilingSomethingNeverFiledReturnsNothing()
    {
        Assert.Null(new GhostArchive().Unfile("m1"));
    }

    // The archive is searched alongside the Mailbox: one filter spans both (§7), so a
    // person you filed last week is still findable.
    [Fact]
    public void GhostsAreSearchableByPersonAndBySubject()
    {
        var archive = new GhostArchive();
        archive.File(Message("m1", who: "plumber@example.com"), Now);
        archive.File(Message("m2", who: "her@example.com", body: "About the boiler"), Now);

        Assert.Single(archive.Matching("plumber"));
        Assert.Single(archive.Matching("boiler"));
        Assert.Equal(2, archive.Matching("dinner").Count);   // both share the subject
        Assert.Empty(archive.Matching("nothing here"));
    }

    // An empty query is not a search for nothing; Filed with no filter shows everything.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyQueryMatchesEverything(string query)
    {
        var archive = new GhostArchive();
        archive.File(Message("m1"), Now);

        Assert.Single(archive.Matching(query));
    }
}
