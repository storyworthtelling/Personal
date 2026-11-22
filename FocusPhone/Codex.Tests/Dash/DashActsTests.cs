using Codex.Domain.Timeline;
using Codex.Infrastructure.Dash;
using Watch.Domain.Dash;
using Xunit;

namespace Codex.Tests.Dash;

/// <summary>
/// The book of the wrist's acts across a process restart (P14 B2): a delete still owed is carried out once, a send is
/// carried out once and answered the same way every time it is heard, and a send cannot be undone. Invented ids.
/// </summary>
public class DashActsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 30, 0, TimeSpan.Zero);

    private static DashActRecord Delete(string act, DateTimeOffset until) =>
        new(act, DashActKind.Delete, [new ActedItem(TimelineItemKind.Message, "sms:41")], until);

    [Fact]
    public void ADeleteIsOwedUntilSettled()
    {
        var acts = new DashActs();
        acts.Record(Delete("act-1", T0.AddSeconds(16)));

        Assert.Equal(["act-1"], acts.Owed().Select(record => record.Act));

        acts.Settle("act-1");

        Assert.Empty(acts.Owed());
        Assert.True(acts.All.Single().Done);
    }

    [Fact]
    public void ATakenBackDeleteIsNotOwed()
    {
        var acts = new DashActs();
        acts.Record(Delete("act-1", T0.AddSeconds(16)));

        acts.Undo("act-1", T0.AddSeconds(5));

        Assert.Empty(acts.Owed());
    }

    [Fact]
    public void ALateUndoLeavesTheDeleteOwed()
    {
        // The window had closed, so the delete stands: it is still owed if it was not yet carried out, and an undo heard
        // again is still too late, never "again".
        var acts = new DashActs();
        acts.Record(Delete("act-1", T0.AddSeconds(16)));

        Assert.Equal(DashOutcome.TooLate, acts.Undo("act-1", T0.AddSeconds(17)).Outcome);
        Assert.Equal(DashOutcome.TooLate, acts.Undo("act-1", T0.AddSeconds(18)).Outcome);
        Assert.Equal(["act-1"], acts.Owed().Select(record => record.Act));
    }

    [Fact]
    public void AFilingIsNeverOwed()
    {
        // A filing is done the moment it is heard (ghost, tombstone, hidden); only the source delete waits, and the sync
        // retries that by the tombstone rule.
        var acts = new DashActs();
        acts.Record(new DashActRecord("act-1", DashActKind.File, [new ActedItem(TimelineItemKind.Call, "41")], T0.AddSeconds(16)));

        Assert.Empty(acts.Owed());
    }

    [Fact]
    public void ASendKeepsItsOutcome()
    {
        var acts = new DashActs();
        Assert.Null(acts.SendAnswer("send-1"));

        acts.Sending("send-1", T0);
        Assert.True(acts.Knows("send-1"));

        acts.Sent("send-1", DashSendOutcome.Sent);
        Assert.Equal(DashSendOutcome.Sent, acts.SendAnswer("send-1"));
    }

    [Fact]
    public void ASendThatNeverFinishedIsAnsweredFailed()
    {
        // The process went while the send was being carried out: Post cannot tell whether it went, and never sends it
        // twice, so a repeat is told it failed and the owner decides.
        var acts = new DashActs();

        acts.Sending("send-1", T0);

        Assert.Equal(DashSendOutcome.Failed, acts.SendAnswer("send-1"));
    }

    [Fact]
    public void ASendCannotBeUndone()
    {
        var acts = new DashActs();
        acts.Sending("send-1", T0);
        acts.Sent("send-1", DashSendOutcome.Sent);

        var undo = acts.Undo("send-1", T0);

        Assert.Equal(DashOutcome.Unknown, undo.Outcome);
        Assert.Empty(undo.Items);
        Assert.Equal(DashSendOutcome.Sent, acts.SendAnswer("send-1"));
        Assert.Empty(acts.Owed());
    }

    [Fact]
    public void AnActIsNoSend()
    {
        var acts = new DashActs();
        acts.Record(Delete("act-1", T0.AddSeconds(16)));

        Assert.Null(acts.SendAnswer("act-1"));
    }

    [Fact]
    public void AnActedItemIsWhatADeleteNeeds()
    {
        var item = new ActedItem(TimelineItemKind.Email, "mail:41", "<41@example.com>").ToItem();

        Assert.Equal((TimelineItemKind.Email, "mail:41", "<41@example.com>"), (item.Kind, item.SourceId, item.MessageId));
    }
}
