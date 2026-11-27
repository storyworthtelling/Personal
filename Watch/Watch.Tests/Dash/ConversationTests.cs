using Watch.Domain.Dash;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// A conversation (W-5.1.1, A10, A14; mock: A conversation): the last few messages, oldest first, the owner's on the
/// right; reading it on the wrist reads it on the phone, through the newest message shown; a reply the wrist sent shows
/// at once and gives way to Post's own copy when it comes.
/// </summary>
public class ConversationTests
{
    private static readonly DashThread Lee = new(LeeKey, "Lee", Group: false, [],
    [
        new DashThreadMessage("m0000000000000001", T0.AddMinutes(-5), Incoming: false, DashMessageType.Text, "Are you bringing the oranges?", null, []),
        new DashThreadMessage("m0000000000000002", T0.AddMinutes(-2), Incoming: true, DashMessageType.Text, "Running late. I’ll be at the field by four.", null, []),
    ]);

    [Fact]
    public void BubblesAreOldestFirst()
    {
        var bubbles = Conversation.Bubbles(Lee, []);

        Assert.Equal(["Are you bringing the oranges?", "Running late. I’ll be at the field by four."], bubbles.Select(bubble => bubble.Words));
    }

    [Fact]
    public void BubblesAreOldestFirstWhateverPostsOrder()
    {
        var reversed = Lee with { Messages = [.. Lee.Messages.Reverse()] };

        Assert.Equal(Conversation.Bubbles(Lee, []), Conversation.Bubbles(reversed, []));
    }

    [Fact]
    public void MineIsTheOwners() =>
        Assert.Equal([true, false], Conversation.Bubbles(Lee, []).Select(bubble => bubble.Mine));

    [Fact]
    public void AGroupNamesItsSenders()
    {
        var bubbles = Conversation.Bubbles(GroupThread, []);

        Assert.Equal([null, "Robin", "Lee", "Sam", null], bubbles.Select(bubble => bubble.From));
    }

    [Fact]
    public void APersonsConversationNamesNoSender()
    {
        var named = Lee with { Messages = [.. Lee.Messages.Select(message => message with { From = message.Incoming ? "Lee" : null })] };

        Assert.All(Conversation.Bubbles(named, []), bubble => Assert.Null(bubble.From));
    }

    [Theory]
    [InlineData(DashMessageType.Text, DashAttachmentType.Photo, "Photo")]
    [InlineData(DashMessageType.Text, DashAttachmentType.Video, "Video")]
    [InlineData(DashMessageType.Text, DashAttachmentType.File, "Attachment")]
    [InlineData(DashMessageType.Voicemail, DashAttachmentType.File, "Voicemail")]
    public void NoWordsSayWhatItIs(DashMessageType type, DashAttachmentType attachment, string expected)
    {
        var thread = Lee with { Messages = [new DashThreadMessage("m0000000000000003", T0, true, type, "  ", null, [new DashAttachment("a0000000000000003", attachment)])] };

        Assert.Equal(expected, Assert.Single(Conversation.Bubbles(thread, [])).Words);
    }

    [Fact]
    public void ACallWithNoWordsSaysCall()
    {
        var thread = Lee with { Messages = [new DashThreadMessage(CallId, T0, true, DashMessageType.Call, "", null, [])] };

        Assert.Equal("Call", Assert.Single(Conversation.Bubbles(thread, [])).Words);
    }

    [Fact]
    public void PostsWordsAreShownAsTheyCame()
    {
        // Post already words a call, a voicemail and a picture its own way ("Missed call", "Photo").
        Assert.Equal(GroupThread.Messages.Select(message => message.Text), Conversation.Bubbles(GroupThread, []).Select(bubble => bubble.Words));
    }

    [Fact]
    public void ATextSentHereShowsAtOnce()
    {
        var sent = new SentHere("send-1", "No rush, I’ll save you a spot by the bench.", T0);

        var last = Conversation.Bubbles(Lee, [sent])[^1];

        Assert.Equal(("send-1", true, "No rush, I’ll save you a spot by the bench."), (last.Id, last.Mine, last.Words));
    }

    [Fact]
    public void PostsCopyReplacesTheOneSentHere()
    {
        var sent = new SentHere("send-1", "No rush, I’ll save you a spot by the bench.", T0);
        var arrived = Lee with
        {
            Messages = [.. Lee.Messages, new DashThreadMessage("m0000000000000004", T0.AddSeconds(20), false, DashMessageType.Text, "No rush, I’ll save you a spot by the bench.", null, [])],
        };

        Assert.Empty(Conversation.StillOwed(arrived, [sent]));
        Assert.Single(Conversation.Bubbles(arrived, [sent]), bubble => bubble.Words.StartsWith("No rush", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOlderTextWithTheSameWordsIsNotTheOneSentHere()
    {
        var sent = new SentHere("send-1", "Are you bringing the oranges?", T0);

        Assert.Equal([sent], Conversation.StillOwed(Lee, [sent]));
    }

    [Fact]
    public void ThePhonesClockMayBeALittleBehind()
    {
        var sent = new SentHere("send-1", "On my way.", T0);
        var arrived = Lee with { Messages = [.. Lee.Messages, new DashThreadMessage("m0000000000000005", T0.AddMinutes(-1), false, DashMessageType.Text, "On my way.", null, [])] };

        Assert.Empty(Conversation.StillOwed(arrived, [sent]));
    }

    [Fact]
    public void AnIncomingTextIsNeverTheOneSentHere()
    {
        var sent = new SentHere("send-1", "On my way.", T0);
        var arrived = Lee with { Messages = [.. Lee.Messages, new DashThreadMessage("m0000000000000006", T0.AddSeconds(5), true, DashMessageType.Text, "On my way.", null, [])] };

        Assert.Equal([sent], Conversation.StillOwed(arrived, [sent]));
    }

    [Fact]
    public void TheReadIsThroughTheNewestShown()
    {
        Assert.Equal(new DashRead(LeeKey, T0.AddMinutes(-2)), Conversation.ReadFor(Lee));
        Assert.Equal(new DashRead(GroupKey, T0.AddMinutes(-10)), Conversation.ReadFor(GroupThread));
    }

    [Fact]
    public void AnEmptyConversationIsNotRead() => Assert.Null(Conversation.ReadFor(Lee with { Messages = [] }));

    [Fact]
    public void AGroupIsRepliedToAll()
    {
        Assert.Equal(DashWords.Reply, Conversation.ReplyLabel(Lee));
        Assert.Equal(DashWords.ReplyToAll, Conversation.ReplyLabel(GroupThread));
    }
}
