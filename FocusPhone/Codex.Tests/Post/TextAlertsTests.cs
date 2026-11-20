using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

// Which Google Messages notifications become Post's own alert. On the locked phone the
// status bar is disabled, which turns off every app's notification sound, vibration and
// heads-up card, and Messages is kept off the shelf — so unless Post raises its own
// notification and buzzes itself, a text arrives with nothing to say so. Messages re-posts
// a conversation's notification for every change (a second message, a reboot, a read
// elsewhere), so only a message newer than the last one alerted may buzz.
public class TextAlertsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1_780_000_000_000);
    private static long Ms(TimeSpan before) => (Now - before).ToUnixTimeMilliseconds();

    private static string KeyFor(string name) => name switch
    {
        "Avery Example" => "5550100001",
        "Blake Example" => "5550100002",
        _ => name.ToLowerInvariant(),
    };

    private static MessagingNotice OneToOne(params NoticeMessage[] messages) =>
        new("conv-7", "Avery Example", IsGroup: false, messages);

    private static MessagingNotice Group(params NoticeMessage[] messages) =>
        new("conv-42", "Book club", IsGroup: true, messages);

    [Fact]
    public void AFreshIncomingMessage_Alerts_OnItsPersonsThread()
    {
        var alert = TextAlerts.Decide(
            OneToOne(new NoticeMessage("Avery Example", Ms(TimeSpan.FromSeconds(5)), "Running late")),
            KeyFor, heardThrough: null, Now);

        Assert.NotNull(alert);
        Assert.Equal("5550100001", alert.ThreadKey);
        Assert.Equal("Avery Example", alert.Title);
        Assert.Equal("Running late", alert.Line);
        Assert.Equal(1, alert.Count);
    }

    [Fact]
    public void ARepostOfAMessageAlreadyAlerted_IsQuiet()
    {
        var sent = Ms(TimeSpan.FromSeconds(30));
        var notice = OneToOne(new NoticeMessage("Avery Example", sent, "Running late"));

        Assert.Null(TextAlerts.Decide(notice, KeyFor, DateTimeOffset.FromUnixTimeMilliseconds(sent), Now));
    }

    [Fact]
    public void OnlyTheMessagesAfterTheLastAlert_Count()
    {
        var first = Ms(TimeSpan.FromMinutes(2));
        var notice = OneToOne(
            new NoticeMessage("Avery Example", first, "Running late"),
            new NoticeMessage("Avery Example", Ms(TimeSpan.FromMinutes(1)), "Ten minutes"),
            new NoticeMessage("Avery Example", Ms(TimeSpan.FromSeconds(3)), "Parking now"));

        var alert = TextAlerts.Decide(notice, KeyFor, DateTimeOffset.FromUnixTimeMilliseconds(first), Now);

        Assert.NotNull(alert);
        Assert.Equal(2, alert.Count);
        Assert.Equal("Parking now", alert.Line);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(Ms(TimeSpan.FromSeconds(3))), alert.HeardThrough);
    }

    [Fact]
    public void TheOwnersOwnMessage_NeverAlerts()
    {
        var notice = OneToOne(new NoticeMessage(null, Ms(TimeSpan.FromSeconds(2)), "On my way"));

        Assert.Null(TextAlerts.Decide(notice, KeyFor, heardThrough: null, Now));
    }

    // With nothing remembered (a cleared install, a conversation never alerted), an unread
    // notification Messages re-posts after a restart must not buzz for an old message.
    [Fact]
    public void WithNothingRemembered_AnOldMessage_IsQuiet()
    {
        var notice = OneToOne(new NoticeMessage("Avery Example", Ms(TimeSpan.FromHours(3)), "Running late"));

        Assert.Null(TextAlerts.Decide(notice, KeyFor, heardThrough: null, Now));
    }

    [Fact]
    public void AGroupMessage_AlertsOnTheGroupsThread_AndNamesWhoSpoke()
    {
        var alert = TextAlerts.Decide(
            Group(new NoticeMessage("Blake Example", Ms(TimeSpan.FromSeconds(4)), "Thursday?")),
            KeyFor, heardThrough: null, Now);

        Assert.NotNull(alert);
        Assert.Equal("group:rcs:conv-42", alert.ThreadKey);
        Assert.Equal("Book club", alert.Title);
        Assert.Equal("Blake Example: Thursday?", alert.Line);
    }

    [Fact]
    public void AMessageWithNoWords_StillSaysSomethingArrived()
    {
        var alert = TextAlerts.Decide(
            OneToOne(new NoticeMessage("Avery Example", Ms(TimeSpan.FromSeconds(4)), null)),
            KeyFor, heardThrough: null, Now);

        Assert.NotNull(alert);
        Assert.Equal("New message", alert.Line);
    }

    [Fact]
    public void TheConversation_IsMessagesOwnId_ElseItsTitle()
    {
        Assert.Equal("conv-7", TextAlerts.ConversationOf(OneToOne()));
        Assert.Equal("title:Avery Example", TextAlerts.ConversationOf(OneToOne() with { ConversationId = null }));
        Assert.Null(TextAlerts.ConversationOf(new MessagingNotice(null, " ", false, [])));
    }
}
