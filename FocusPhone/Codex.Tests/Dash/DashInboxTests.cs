using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Dash;
using Watch.Domain;
using Watch.Domain.Dash;
using Xunit;

namespace Codex.Tests.Dash;

/// <summary>
/// What Post's listener does with each message from the wrist (P14 B2): an ask is answered from the Mailbox's own view,
/// an act becomes the relay's plan for Post's own code to carry out, a send is carried out once however often it is heard,
/// and an answer arriving at Post (the watch's half of a contract) is left alone. Invented people and numbers.
/// </summary>
public class DashInboxTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan Six = TimeSpan.FromSeconds(6);
    private static readonly DashConditions Now = new(T0, Six, BridgeConnected: false, CanSendSms: true);

    private const string Lee = "2125550101";
    private const string Robin = "2125550102";
    private const string LeeMail = "lee@example.com";

    private static TimelineItem Text(string who, int minute, string words = "hi") =>
        new(TimelineItemKind.Message, who, T0.AddMinutes(minute), words, Incoming: true, $"sms:{who}:{minute}");

    private static TimelineItem Call(string who, int minute) =>
        new(TimelineItemKind.Call, who, T0.AddMinutes(minute), "Missed call", Incoming: true, $"call:{who}:{minute}");

    private static TimelineItem Mail(string from, int minute) =>
        new(TimelineItemKind.Email, from, T0.AddMinutes(minute), "See you there.", Incoming: true, $"mail:{from}:{minute}")
        {
            Subject = "Saturday plans",
            FromAddress = from,
            MailClass = MailClass.Human,
            MessageId = $"<{minute}@example.com>",
        };

    private static DashRelay Relay(params TimelineItem[] items) =>
        new(new DashSnapshot(items.ToList(), new HashSet<(TimelineItemKind, string)>(), SpamList.From([]), new DeleteBuffer(),
            new Tombstones(), PerKeyIdentityResolver.Instance, new ReadState(new Dictionary<string, DateTimeOffset>()), GroupNames.Empty,
            key => key == Lee ? "Lee" : key));

    // A relay the turn must not need: an undo, and an answer arriving at Post, are decided without the Mailbox.
    private static DashRelay NoRelay() => throw new InvalidOperationException("The Mailbox was read for a message that needs none.");

    // The Rolodex the turn must not need (B6): only a Rolodex ask and a new text to a contact read the address book.
    private static RolodexRelay NoRolodex() => throw new InvalidOperationException("The address book was read for a message that needs none.");

    private static readonly RolodexPerson LeeInTheBook = new(7, "Lee", ["+1 212-555-0101"]);

    private static RolodexRelay Book() => new([LeeInTheBook, new RolodexPerson(8, "Robin", [Robin])]);

    private static string ContactOf(long contact) => DashIds.Contact(contact.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string KeyOf(string threadKey) => DashIds.Correspondence(threadKey);

    // ---- asks ----

    [Fact]
    public void PeopleWaitingIsAnswered()
    {
        var turn = DashInbox.Turn(new DashPeopleAsk(), () => Relay(Text(Lee, -2), Text(Robin, -9)), NoRolodex, new DashActs(), Now);

        var answer = Assert.IsType<DashAnswer>(turn);
        var people = Assert.IsType<DashPeople>(answer.Reply);
        Assert.Equal([KeyOf(Lee), KeyOf(Robin)], people.People.Select(person => person.Key));
        Assert.Equal((WatchIdentity.DashPeoplePath, DashOutcome.Answered, 2), (answer.Path, answer.Outcome, answer.Count));
    }

    [Fact]
    public void AConversationIsAnswered()
    {
        var turn = DashInbox.Turn(new DashThreadAsk(KeyOf(Lee)), () => Relay(Text(Lee, -9), Text(Lee, -2)), NoRolodex, new DashActs(), Now);

        var answer = Assert.IsType<DashAnswer>(turn);
        var thread = Assert.IsType<DashThread>(answer.Reply);
        Assert.Equal((KeyOf(Lee), "Lee", 2), (thread.Key, thread.Title, thread.Messages.Count));
        Assert.Equal((WatchIdentity.DashThreadPath, 2), (answer.Path, answer.Count));
    }

    [Fact]
    public void AnUnknownConversationIsNotAnswered()
    {
        var turn = DashInbox.Turn(new DashThreadAsk(KeyOf(Robin)), () => Relay(Text(Lee, -2)), NoRolodex, new DashActs(), Now);

        var none = Assert.IsType<DashNoTurn>(turn);
        Assert.Equal((WatchIdentity.DashThreadPath, DashOutcome.Unknown), (none.Path, none.Outcome));
    }

    [Fact]
    public void MailLinesAreAnswered()
    {
        var turn = DashInbox.Turn(new DashMailAsk(), () => Relay(Mail(LeeMail, -3), Text(Lee, -2)), NoRolodex, new DashActs(), Now);

        var answer = Assert.IsType<DashAnswer>(turn);
        Assert.Single(Assert.IsType<DashMail>(answer.Reply).Mail);
        Assert.Equal((WatchIdentity.DashMailPath, 1), (answer.Path, answer.Count));
    }

    [Theory]
    [MemberData(nameof(Answers))]
    public void AnAnswerArrivingAtPostIsLeftAlone(DashMessage message)
    {
        var turn = DashInbox.Turn(message, NoRelay, NoRolodex, new DashActs(), Now);

        var none = Assert.IsType<DashNoTurn>(turn);
        Assert.Equal((message.Path, (DashOutcome?)null), (none.Path, none.Outcome));
    }

    public static TheoryData<DashMessage> Answers() =>
    [
        new DashPeople([]),
        new DashThread(DashIds.Correspondence("2125550101"), "Lee", false, [], []),
        new DashSent("send-1", DashSendOutcome.Sent),
        new DashMail([]),
        new DashPhoto(DashIds.Attachment("sms:2125550101:-2", "part:7"), [0xFF, 0xD8, 0xFF, 0xD9]),
        new DashRolodex(0, 1, DashIds.Edition("none"), []),
    ];

    // ---- dash/send ----

    [Fact]
    public void ASendIsCarriedOutOnce()
    {
        var acts = new DashActs();
        var send = new DashSend("send-1", KeyOf(Lee), [], "On my way.");

        var first = Assert.IsType<DashSendTurn>(DashInbox.Turn(send, () => Relay(Text(Lee, -2)), NoRolodex, acts, Now));
        Assert.Equal([Lee], first.Plan.Recipients);

        var answer = DashInbox.Delivered(send, new TextDelivery(TextSendPath.NativeSms, BridgeTried: false), acts);
        Assert.Equal(new DashSent("send-1", DashSendOutcome.Sent), answer);

        // The same send heard again is answered as it went, and nothing is sent twice.
        var again = Assert.IsType<DashAnswer>(DashInbox.Turn(send, NoRelay, NoRolodex, acts, Now));
        Assert.Equal(new DashSent("send-1", DashSendOutcome.Sent), again.Reply);
        Assert.Equal(DashOutcome.Again, again.Outcome);
    }

    [Fact]
    public void ASendHeardWhileItIsCarriedOutIsNotSentAgain()
    {
        var acts = new DashActs();
        var send = new DashSend("send-1", KeyOf(Lee), [], "On my way.");
        DashInbox.Turn(send, () => Relay(Text(Lee, -2)), NoRolodex, acts, Now);

        var again = Assert.IsType<DashAnswer>(DashInbox.Turn(send, NoRelay, NoRolodex, acts, Now));

        Assert.Equal(new DashSent("send-1", DashSendOutcome.Failed), again.Reply);
    }

    [Theory]
    [InlineData(false, false, DashSendOutcome.NoRoute, DashOutcome.NoRoute)]
    public void ASendNothingCanCarryIsAnsweredAtOnce(bool bridge, bool sms, DashSendOutcome said, DashOutcome logged)
    {
        var acts = new DashActs();
        var send = new DashSend("send-1", KeyOf(Lee), [], "On my way.");

        var answer = Assert.IsType<DashAnswer>(DashInbox.Turn(send, () => Relay(Text(Lee, -2)), NoRolodex, acts, Now with { BridgeConnected = bridge, CanSendSms = sms }));

        Assert.Equal((new DashSent("send-1", said), logged), (answer.Reply, answer.Outcome));
        Assert.Equal(said, acts.SendAnswer("send-1"));
    }

    [Fact]
    public void ASendToNobodyIsAnsweredAtOnce()
    {
        var acts = new DashActs();
        var send = new DashSend("send-1", KeyOf(Robin), [], "On my way.");

        var answer = Assert.IsType<DashAnswer>(DashInbox.Turn(send, () => Relay(Text(Lee, -2)), NoRolodex, acts, Now));

        Assert.Equal((new DashSent("send-1", DashSendOutcome.Nowhere), DashOutcome.Nowhere), (answer.Reply, answer.Outcome));
        Assert.Equal(DashSendOutcome.Nowhere, acts.SendAnswer("send-1"));
    }

    [Theory]
    [InlineData(TextSendPath.Bridge, true, false, DashSendOutcome.Sent)]
    [InlineData(TextSendPath.NativeSms, true, false, DashSendOutcome.Sent)]
    [InlineData(TextSendPath.GroupMms, false, false, DashSendOutcome.Sent)]
    [InlineData(TextSendPath.Unavailable, false, false, DashSendOutcome.NoRoute)]
    [InlineData(TextSendPath.Unavailable, true, false, DashSendOutcome.Failed)]
    [InlineData(TextSendPath.NativeSms, false, true, DashSendOutcome.Failed)]
    [InlineData(TextSendPath.GroupMms, true, true, DashSendOutcome.Failed)]
    public void TheAnswerIsWhatThePhoneDid(TextSendPath path, bool bridgeTried, bool failed, DashSendOutcome expected)
    {
        var delivery = new TextDelivery(path, bridgeTried, failed ? new InvalidOperationException("refused") : null);

        Assert.Equal(expected, DashInbox.OutcomeOf(delivery));
    }

    // ---- dash/file, dash/delete, dash/undo ----

    [Fact]
    public void AFileFromTheWristIsAFiling()
    {
        var turn = DashInbox.Turn(new DashFile("act-1", KeyOf(Lee), [], Six), () => Relay(Text(Lee, -9), Call(Lee, -2)), NoRolodex, new DashActs(), Now);

        var act = Assert.IsType<DashActTurn>(turn);
        Assert.Equal((WatchIdentity.DashFilePath, "act-1", DashActKind.File, DashOutcome.Filed, 2), (act.Path, act.Act, act.Kind, act.Plan.Outcome, act.Plan.Items.Count));
    }

    [Fact]
    public void ADeleteFromTheWristIsHeld()
    {
        var acts = new DashActs();
        var turn = DashInbox.Turn(new DashDelete("act-1", KeyOf(Lee), [], Six), () => Relay(Text(Lee, -2)), NoRolodex, acts, Now);

        var act = Assert.IsType<DashActTurn>(turn);
        Assert.Equal((WatchIdentity.DashDeletePath, DashActKind.Delete, DashOutcome.Held), (act.Path, act.Kind, act.Plan.Outcome));
        Assert.Equal(T0 + act.Plan.Window, Assert.Single(acts.Owed()).Until);
    }

    [Fact]
    public void ADeletedMailKeepsWhatItsDeleteNeeds()
    {
        // A mail line deleted from the wrist is trashed at Gmail by its message id, even if the phone restarts in the window.
        var mail = Mail(LeeMail, -3);
        var acts = new DashActs();

        DashInbox.Turn(new DashDelete("act-1", null, [DashIds.Message(mail.SourceId)], Six), () => Relay(mail), NoRolodex, acts, Now);

        Assert.Equal(new ActedItem(TimelineItemKind.Email, mail.SourceId, "<-3@example.com>"), acts.Owed().Single().Items.Single());
    }

    [Fact]
    public void AnUndoNeedsNoMailbox()
    {
        var acts = new DashActs();
        DashInbox.Turn(new DashFile("act-1", KeyOf(Lee), [], Six), () => Relay(Text(Lee, -2)), NoRolodex, acts, Now);

        var turn = DashInbox.Turn(new DashUndo("act-1"), NoRelay, NoRolodex, acts, Now with { Now = T0.AddSeconds(3) });

        var undo = Assert.IsType<DashUndoTurn>(turn);
        Assert.Equal((DashOutcome.TakenBack, DashActKind.File, 1), (undo.Plan.Outcome, undo.Plan.Kind, undo.Plan.Items.Count));
    }

    [Fact]
    public void TheSameActTwiceIsDoneOnce()
    {
        var acts = new DashActs();
        var file = new DashFile("act-1", KeyOf(Lee), [], Six);
        DashInbox.Turn(file, () => Relay(Text(Lee, -2)), NoRolodex, acts, Now);

        var again = Assert.IsType<DashActTurn>(DashInbox.Turn(file, () => Relay(Text(Lee, -2)), NoRolodex, acts, Now));

        Assert.Equal(DashOutcome.Again, again.Plan.Outcome);
        Assert.Empty(again.Plan.Items);
    }

    // ---- dash/photo (B5) ----

    private static TimelineItem Picture(string who, int minute) =>
        Text(who, minute, "") with { Attachments = [new AttachmentRef("part:7", "photo.jpg", "image/jpeg", 2_400_000)] };

    private static string PhotoOf(TimelineItem item) => DashIds.Attachment(item.SourceId, item.Attachments[0].Id);

    private static readonly byte[] Shrunk = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0xFF, 0xD9];

    [Fact]
    public void APhotoIsFetchedForTheWrist()
    {
        var picture = Picture(Lee, -2);

        var turn = DashInbox.Turn(new DashPhotoAsk(PhotoOf(picture)), () => Relay(Text(Lee, -9), picture), NoRolodex, new DashActs(), Now);

        var photo = Assert.IsType<DashPhotoTurn>(turn);
        Assert.Equal((PhotoOf(picture), picture.SourceId, "part:7"), (photo.Ask.Id, photo.Plan.Item.SourceId, photo.Plan.Attachment.Id));
        Assert.Equal(WatchIdentity.DashPhotoPath, photo.Path);
    }

    [Fact]
    public void AnUnknownPhotoIsNotAnswered()
    {
        var turn = DashInbox.Turn(new DashPhotoAsk(DashIds.Attachment("sms:elsewhere", "part:1")), () => Relay(Picture(Lee, -2)), NoRolodex, new DashActs(), Now);

        var none = Assert.IsType<DashNoTurn>(turn);
        Assert.Equal((WatchIdentity.DashPhotoPath, (DashOutcome?)DashOutcome.Unknown), (none.Path, none.Outcome));
    }

    [Fact]
    public void AShrunkPhotoIsTheAnswer()
    {
        var picture = Picture(Lee, -2);
        var turn = Assert.IsType<DashPhotoTurn>(DashInbox.Turn(new DashPhotoAsk(PhotoOf(picture)), () => Relay(picture), NoRolodex, new DashActs(), Now));

        var answer = Assert.IsType<DashAnswer>(DashInbox.Photo(turn, Shrunk));

        var photo = Assert.IsType<DashPhoto>(answer.Reply);
        Assert.Equal((PhotoOf(picture), Shrunk), (photo.Id, photo.Jpeg));
        Assert.Equal((WatchIdentity.DashPhotoPath, DashOutcome.Answered, 1), (answer.Path, answer.Outcome, answer.Count));
    }

    [Fact]
    public void APhotoThatWouldNotShrinkIsNotAnswered()
    {
        var picture = Picture(Lee, -2);
        var turn = Assert.IsType<DashPhotoTurn>(DashInbox.Turn(new DashPhotoAsk(PhotoOf(picture)), () => Relay(picture), NoRolodex, new DashActs(), Now));
        var tooLarge = new byte[DashLimits.PhotoBytes + 1];
        Shrunk.CopyTo(tooLarge, 0);

        Assert.Equal(DashOutcome.Unfetched, Assert.IsType<DashNoTurn>(DashInbox.Photo(turn, null)).Outcome);
        Assert.Equal(DashOutcome.Unfetched, Assert.IsType<DashNoTurn>(DashInbox.Photo(turn, tooLarge)).Outcome);
        Assert.Equal(DashOutcome.Unfetched, Assert.IsType<DashNoTurn>(DashInbox.Photo(turn, [0x89, 0x50, 0x4E, 0x47])).Outcome);
    }

    [Fact]
    public void AnUndoHeardBeforeItsActKeepsItFromHappening()
    {
        // The wrist sends an undo only once its act has gone, but two messages may cross in either order: an undo that
        // comes first is kept, so the act that follows it is done never rather than done for good.
        var acts = new DashActs();

        var undo = Assert.IsType<DashUndoTurn>(DashInbox.Turn(new DashUndo("act-1"), NoRelay, NoRolodex, acts, Now));
        var late = Assert.IsType<DashActTurn>(DashInbox.Turn(new DashFile("act-1", KeyOf(Lee), [], Six), () => Relay(Text(Lee, -2)), NoRolodex, acts, Now));

        Assert.Equal(DashOutcome.Unknown, undo.Plan.Outcome);
        Assert.Equal(DashOutcome.Again, late.Plan.Outcome);
        Assert.Empty(late.Plan.Items);
        Assert.Empty(acts.Owed());
    }

    // ---- dash/read, dash/call-note ----

    [Fact]
    public void AReadIsMarked()
    {
        var items = new[] { Text(Lee, -9), Text(Lee, -2) };

        var turn = DashInbox.Turn(new DashRead(KeyOf(Lee), items[1].TimestampUtc), () => Relay(items), NoRolodex, new DashActs(), Now);

        var read = Assert.IsType<DashReadTurn>(turn);
        Assert.Equal((DashOutcome.Marked, Lee, items[1].TimestampUtc), (read.Plan.Outcome, read.Plan.ThreadKey, read.Plan.Stamp));
    }

    [Fact]
    public void ANoteIsKept()
    {
        var call = Call(Lee, -5);

        var turn = DashInbox.Turn(new DashCallNote(DashIds.Message(call.SourceId), null, "Pick up at four."), () => Relay(call), NoRolodex, new DashActs(), Now);

        var note = Assert.IsType<DashNoteTurn>(turn);
        Assert.Equal(DashOutcome.Kept, note.Plan.Outcome);
        Assert.Equal((Lee, "Pick up at four."), (note.Plan.Note!.ContactKey, note.Plan.Note.Snippet));
    }

    // ---- B6: the Rolodex, and a new text to a contact ----

    [Fact]
    public void TheRolodexIsAnswered()
    {
        var turn = DashInbox.Turn(new DashRolodexAsk(0), NoRelay, Book, new DashActs(), Now);

        var answer = Assert.IsType<DashAnswer>(turn);
        var page = Assert.IsType<DashRolodex>(answer.Reply);
        Assert.Equal(["Lee", "Robin"], page.Contacts.Select(contact => contact.Name));
        Assert.Equal((WatchIdentity.DashRolodexPath, DashOutcome.Answered, 2), (answer.Path, answer.Outcome, answer.Count));
    }

    [Fact]
    public void APagePastTheLastIsNotAnswered()
    {
        var none = Assert.IsType<DashNoTurn>(DashInbox.Turn(new DashRolodexAsk(1), NoRelay, Book, new DashActs(), Now));

        Assert.Equal((WatchIdentity.DashRolodexPath, DashOutcome.Unknown), (none.Path, none.Outcome));
    }

    [Fact]
    public void ANewTextGoesToTheContactsNumber()
    {
        // The wrist names the contact; Post finds the number on the phone and takes the phone's own road, as a reply does.
        var send = new DashSend("send-1", null, [], "Running five minutes late.") { Contact = ContactOf(7) };

        var turn = Assert.IsType<DashSendTurn>(DashInbox.Turn(send, NoRelay, Book, new DashActs(), Now));

        Assert.Equal(["+1 212-555-0101"], turn.Plan.Recipients);
        Assert.Null(turn.Plan.ConversationId);
        Assert.Equal((false, TextSendPath.NativeSms, (DashSendOutcome?)null), (turn.Plan.TryBridge, turn.Plan.Otherwise, turn.Plan.Refused));
    }

    [Fact]
    public void ANewTextTriesTheBridgeFirst()
    {
        var send = new DashSend("send-1", null, [], "On my way.") { Contact = ContactOf(8) };

        var turn = Assert.IsType<DashSendTurn>(DashInbox.Turn(send, NoRelay, Book, new DashActs(), Now with { BridgeConnected = true }));

        Assert.Equal([Robin], turn.Plan.Recipients);
        Assert.True(turn.Plan.TryBridge);
    }

    [Fact]
    public void ANewTextToSomeoneUnknownIsNowhere()
    {
        var acts = new DashActs();
        var send = new DashSend("send-1", null, [], "On my way.") { Contact = ContactOf(99) };

        var answer = Assert.IsType<DashAnswer>(DashInbox.Turn(send, NoRelay, Book, acts, Now));

        Assert.Equal(new DashSent("send-1", DashSendOutcome.Nowhere), answer.Reply);
        Assert.Equal(DashOutcome.Nowhere, answer.Outcome);
        Assert.Equal(DashSendOutcome.Nowhere, acts.SendAnswer("send-1"));
    }

    [Fact]
    public void ANewTextWithNothingToCarryItIsNoRoute()
    {
        var send = new DashSend("send-1", null, [], "On my way.") { Contact = ContactOf(7) };

        var answer = Assert.IsType<DashAnswer>(DashInbox.Turn(send, NoRelay, Book, new DashActs(), Now with { CanSendSms = false }));

        Assert.Equal(new DashSent("send-1", DashSendOutcome.NoRoute), answer.Reply);
    }

    [Fact]
    public void ANewTextIsSentOnce()
    {
        // The same rule as a reply's (B2): heard again under the same name, it is answered from the book, never sent again.
        var acts = new DashActs();
        var send = new DashSend("send-1", null, [], "On my way.") { Contact = ContactOf(7) };
        var first = Assert.IsType<DashSendTurn>(DashInbox.Turn(send, NoRelay, Book, acts, Now));
        DashInbox.Delivered(send, new TextDelivery(TextSendPath.NativeSms, BridgeTried: false, Failure: null), acts);

        var again = Assert.IsType<DashAnswer>(DashInbox.Turn(send, NoRelay, NoRolodex, acts, Now));

        Assert.NotNull(first);
        Assert.Equal((new DashSent("send-1", DashSendOutcome.Sent), DashOutcome.Again), (again.Reply, again.Outcome));
    }
}
