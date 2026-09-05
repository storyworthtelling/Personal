using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// The owner's expectation, stated plainly: a sync happens every time they do something,
/// rather than only when a screen opens or the half-hourly worker wakes. These pin the one
/// place that is not obvious — an undoable act must not reach the source until its window
/// has closed, or "undo" is a lie about everywhere but this phone.
/// </summary>
public class SyncMomentTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(6);

    [Fact]
    public void FilingWaitsForTheUndoWindow()
    {
        // Filing owes a delete at the source. Carry it out inside the window and undo can
        // put the message back in Post but never back in the inbox it came from.
        Assert.Equal(Window, SyncMoment.Delay(PostAction.Filed, Window));
    }

    [Fact]
    public void DeletingWaitsForTheUndoWindow()
    {
        Assert.Equal(Window, SyncMoment.Delay(PostAction.Deleted, Window));
    }

    [Fact]
    public void SendingSyncsAtOnce()
    {
        // Nothing about a sent message is undoable, so there is nothing to wait for, and
        // the sent copy should appear in the correspondence without a wait for a worker.
        // This covers starting a correspondence too: that is just its first send.
        Assert.Equal(TimeSpan.Zero, SyncMoment.Delay(PostAction.Sent, Window));
    }

    [Fact]
    public void AZeroWindowNeverDefersAnything()
    {
        // Defensive: if the undo window is ever tuned to nothing, filing still syncs.
        Assert.Equal(TimeSpan.Zero, SyncMoment.Delay(PostAction.Filed, TimeSpan.Zero));
    }
}
