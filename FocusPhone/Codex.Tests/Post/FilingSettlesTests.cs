using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// A filed message is out of the Mailbox at once, but it must not turn up in Filed until
/// its undo window has closed. Seeing it land in Filed while the fold is still offering to
/// put it back reads as the app having already decided — the owner's words: it "does read
/// as broken".
///
/// The ghost is still WRITTEN immediately, and that is not negotiable: the live row is
/// deleted the moment filing happens, so between then and the window closing the ghost is
/// the only copy of the message that exists anywhere. This hides it from one screen; it
/// never delays keeping it.
/// </summary>
public class FilingSettlesTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(6);
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static TimelineItem Message(string id) => new(
        TimelineItemKind.Email,
        ContactKey: "someone@example.com",
        TimestampUtc: Now,
        Snippet: "Snippet",
        Incoming: true,
        SourceId: id)
    {
        Subject = "Subject " + id,
        Body = "Body",
    };

    private static GhostArchive ArchiveWith(params (string Id, DateTimeOffset FiledAt)[] filings)
    {
        var archive = new GhostArchive();
        foreach (var (id, filedAt) in filings)
        {
            archive.File(Message(id), filedAt);
        }
        return archive;
    }

    [Fact]
    public void AGhostInsideItsUndoWindowIsNotShownInFiled()
    {
        var archive = ArchiveWith(("just-filed", Now.AddSeconds(-1)));

        Assert.Empty(archive.Settled(query: "", Now, Window));
    }

    [Fact]
    public void AGhostAppearsOnceTheWindowHasClosed()
    {
        var archive = ArchiveWith(("filed", Now.AddSeconds(-7)));

        var settled = archive.Settled(query: "", Now, Window);

        Assert.Equal("filed", Assert.Single(settled).Item.SourceId);
    }

    [Fact]
    public void TheGhostIsKeptWhileItIsHidden()
    {
        // The whole point: hidden from a screen, never withheld from the archive. A
        // process death inside the window must not lose the only copy.
        var archive = ArchiveWith(("just-filed", Now.AddSeconds(-1)));

        Assert.Single(archive.All);
    }

    [Fact]
    public void SettlingStillHonoursTheSearchQuery()
    {
        var archive = ArchiveWith(("alpha", Now.AddSeconds(-30)), ("beta", Now.AddSeconds(-30)));

        var found = archive.Settled("alpha", Now, Window);

        Assert.Equal("alpha", Assert.Single(found).Item.SourceId);
    }

    [Fact]
    public void AGhostFiledOnAnotherDeviceOrBeforeARestartIsVisible()
    {
        // Restored from the Drive backup, or filed before the app was killed: the window
        // is long past, so nothing should hide it.
        var archive = ArchiveWith(("restored", Now.AddDays(-40)));

        Assert.Single(archive.Settled(query: "", Now, Window));
    }
}
