using Codex.Domain.Timeline;
using Codex.Infrastructure.Dash;
using Watch.Domain.Dash;
using Xunit;

namespace Codex.Tests.Dash;

/// <summary>
/// The book of the wrist's acts, as Post keeps it on the phone across a process restart (P14 B2): a duplicated act heard
/// after the restart is still known, an undo still takes back what its act took, and a delete still owed is still owed.
/// A record that will not read starts a new book rather than stopping the relay. Invented ids.
/// </summary>
public class DashActsCodecTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 30, 0, TimeSpan.Zero);

    private static readonly DashActRecord[] Records =
    [
        new("act-1", DashActKind.File, [new ActedItem(TimelineItemKind.Message, "sms:41"), new ActedItem(TimelineItemKind.Call, "17")], T0.AddSeconds(16)),
        new("act-2", DashActKind.Delete, [new ActedItem(TimelineItemKind.Email, "mail:9", "<9@example.com>")], T0.AddSeconds(40), Taken: true),
        new("act-3", DashActKind.Delete, [new ActedItem(TimelineItemKind.Voicemail, "vm:3")], T0.AddSeconds(50), Done: true),
        new("send-1", DashActKind.Send, [], T0, Sent: DashSendOutcome.NoRoute),
        new("send-2", DashActKind.Send, [], T0),
    ];

    [Fact]
    public void TheBookRoundTrips()
    {
        var read = DashActsCodec.Decode(DashActsCodec.Encode(Records));

        Assert.False(read.Unread);
        Assert.Equal(Records.Length, read.Records.Count);
        for (var index = 0; index < Records.Length; index++)
        {
            var (expected, actual) = (Records[index], read.Records[index]);
            Assert.Equal((expected.Act, expected.Kind, expected.Until, expected.Taken, expected.Done, expected.Sent),
                (actual.Act, actual.Kind, actual.Until, actual.Taken, actual.Done, actual.Sent));
            Assert.Equal(expected.Items, actual.Items);
        }
    }

    [Fact]
    public void TheBookComesBackAfterARestart()
    {
        var before = new DashActs();
        foreach (var record in Records)
        {
            before.Record(record);
        }

        var after = DashActs.From(DashActsCodec.Decode(DashActsCodec.Encode(before.All)).Records);

        Assert.True(after.Knows("act-1"));
        Assert.Equal(DashSendOutcome.NoRoute, after.SendAnswer("send-1"));
        Assert.Equal(DashSendOutcome.Failed, after.SendAnswer("send-2"));
        Assert.Empty(after.Owed());
        var undo = after.Undo("act-1", T0);
        Assert.Equal(DashOutcome.TakenBack, undo.Outcome);
        Assert.Equal(["sms:41", "17"], undo.Items.Select(item => item.SourceId));
    }

    [Fact]
    public void AnOwedDeleteIsStillOwedAfterARestart()
    {
        var before = new DashActs();
        before.Record(new DashActRecord("act-1", DashActKind.Delete, [new ActedItem(TimelineItemKind.Message, "sms:41")], T0.AddSeconds(16)));

        var after = DashActs.From(DashActsCodec.Decode(DashActsCodec.Encode(before.All)).Records);

        var owed = Assert.Single(after.Owed());
        Assert.Equal((T0.AddSeconds(16), "sms:41"), (owed.Until, owed.Items.Single().SourceId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NothingStoredIsAnEmptyBook(string? stored)
    {
        var read = DashActsCodec.Decode(stored);

        Assert.False(read.Unread);
        Assert.Empty(read.Records);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"v":1}""")]
    [InlineData("""{"v":"1","acts":[]}""")]
    [InlineData("""{"v":1,"acts":{}}""")]
    [InlineData("""{"v":1,"acts":[{"kind":"file","until":0,"taken":false,"done":false,"items":[]}]}""")]
    [InlineData("""{"v":1,"acts":[{"act":"a","kind":"undo","until":0,"taken":false,"done":false,"items":[]}]}""")]
    [InlineData("""{"v":1,"acts":[{"act":"a","kind":"file","taken":false,"done":false,"items":[]}]}""")]
    [InlineData("""{"v":1,"acts":[{"act":"a","kind":"file","until":0,"taken":"no","done":false,"items":[]}]}""")]
    [InlineData("""{"v":1,"acts":[{"act":"a","kind":"file","until":0,"taken":false,"done":false,"items":[{"kind":9,"source":"sms:1"}]}]}""")]
    [InlineData("""{"v":1,"acts":[{"act":"a","kind":"file","until":0,"taken":false,"done":false,"items":[{"kind":1}]}]}""")]
    [InlineData("""{"v":1,"acts":[{"act":"a","kind":"send","until":0,"taken":false,"done":false,"items":[],"sent":"maybe"}]}""")]
    public void ADamagedBookIsUnread(string stored)
    {
        var read = DashActsCodec.Decode(stored);

        Assert.True(read.Unread);
        Assert.Empty(read.Records);
    }

    [Fact]
    public void ANewerBookIsUnread()
    {
        var read = DashActsCodec.Decode("""{"v":2,"acts":[]}""");

        Assert.True(read.Unread);
        Assert.Empty(read.Records);
    }

    [Fact]
    public void TheWordsAreFrozen()
    {
        // Stored on the phone: a renamed enum or field must not turn the book into a new one.
        var stored = DashActsCodec.Encode(
        [
            new("act-1", DashActKind.Delete, [new ActedItem(TimelineItemKind.Email, "mail:9", "<9@example.com>")], DateTimeOffset.FromUnixTimeMilliseconds(1_000), Taken: true),
            new("send-1", DashActKind.Send, [], DateTimeOffset.FromUnixTimeMilliseconds(2_000), Done: true, Sent: DashSendOutcome.NoRoute),
            new("act-2", DashActKind.File, [new ActedItem(TimelineItemKind.Message, "sms:1")], DateTimeOffset.FromUnixTimeMilliseconds(3_000)),
        ]);

        Assert.Equal(
            """{"v":1,"acts":[""" +
            """{"act":"act-1","kind":"delete","until":1000,"taken":true,"done":false,"items":[{"kind":2,"source":"mail:9","message":"<9@example.com>"}]},""" +
            """{"act":"send-1","kind":"send","until":2000,"taken":false,"done":true,"items":[],"sent":"no-route"},""" +
            """{"act":"act-2","kind":"file","until":3000,"taken":false,"done":false,"items":[{"kind":1,"source":"sms:1"}]}""" +
            "]}",
            stored);
    }
}
