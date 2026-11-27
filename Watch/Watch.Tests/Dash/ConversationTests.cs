using Watch.Domain.Dash;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// A conversation (W-5.1.1, A10, A14; mock: A conversation): the last few messages, oldest first, the owner's on the
/// right; reading it on the wrist reads it on the phone, through the newest message shown; a reply the wrist sent shows
/// at once and gives way to Post's own copy when it comes. A photo is shown as the mockup draws it (mock: A group text
/// with a photo, W-5.1.3), fetched on its own by its id, newest first.
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

    // ---- photos (W-5.1.3) ----

    private static DashThreadMessage Picture(string id, int minute, string words, params DashAttachment[] attachments) =>
        new(id, T0.AddMinutes(minute), Incoming: true, DashMessageType.Text, words, null, attachments);

    [Fact]
    public void APhotoIsShownAsTheMockupDrawsIt()
    {
        // Robin's photo in the group: the picture, and no bubble saying "Photo" under it; the video beside it is named.
        var robin = Conversation.Bubbles(GroupThread, [])[1];

        Assert.Equal([PhotoId], robin.Photos);
        Assert.False(robin.Said);
        Assert.Equal("Photo", robin.Words);
        Assert.Equal("Robin", robin.From);
    }

    [Fact]
    public void APhotoWithWordsKeepsThem()
    {
        var thread = Lee with { Messages = [Picture("m0000000000000007", -1, "Look at the oranges.", new DashAttachment(PhotoId, DashAttachmentType.Photo))] };

        var bubble = Assert.Single(Conversation.Bubbles(thread, []));

        Assert.Equal([PhotoId], bubble.Photos);
        Assert.True(bubble.Said);
        Assert.Equal("Look at the oranges.", bubble.Words);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Photo")]
    public void APhotoWithNoWordsOfItsOwnHasNoBubble(string words)
    {
        var thread = Lee with { Messages = [Picture("m0000000000000007", -1, words, new DashAttachment(PhotoId, DashAttachmentType.Photo))] };

        Assert.False(Assert.Single(Conversation.Bubbles(thread, [])).Said);
    }

    [Fact]
    public void EveryPhotoOfAMessageIsShownAndNothingElse()
    {
        var thread = Lee with
        {
            Messages =
            [
                Picture("m0000000000000007", -1, "", new DashAttachment("a0000000000000001", DashAttachmentType.Photo),
                    new DashAttachment("a0000000000000002", DashAttachmentType.Video), new DashAttachment("a0000000000000003", DashAttachmentType.Photo)),
            ],
        };

        Assert.Equal(["a0000000000000001", "a0000000000000003"], Assert.Single(Conversation.Bubbles(thread, [])).Photos);
    }

    [Fact]
    public void AMessageWithNoPhotoIsAlwaysSaid()
    {
        Assert.All(Conversation.Bubbles(GroupThread, []).Where(bubble => bubble.Photos.Count == 0), bubble =>
        {
            Assert.True(bubble.Said);
            Assert.Empty(bubble.Photos);
        });
    }

    [Fact]
    public void PhotosAreAskedForNewestFirst()
    {
        var thread = Lee with
        {
            Messages =
            [
                Picture("m0000000000000007", -9, "", new DashAttachment("a0000000000000001", DashAttachmentType.Photo)),
                Picture("m0000000000000008", -3, "", new DashAttachment("a0000000000000002", DashAttachmentType.Photo),
                    new DashAttachment("a0000000000000003", DashAttachmentType.Photo)),
                Picture("m0000000000000009", -1, "", new DashAttachment("a0000000000000004", DashAttachmentType.Photo)),
            ],
        };

        var asked = Conversation.PhotosToAsk(Conversation.Bubbles(thread, []), id => id == "a0000000000000004");

        Assert.Equal(["a0000000000000002", "a0000000000000003", "a0000000000000001"], asked);
    }

    [Fact]
    public void TheSameBubblesAreEqual()
    {
        // Two answers that say the same are the same conversation, photos and all.
        Assert.Equal(Conversation.Bubbles(GroupThread, []), Conversation.Bubbles(GroupThread with { Messages = [.. GroupThread.Messages] }, []));
    }
}
