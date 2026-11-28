using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Dash;
using Storyworthtelling.Folds;
using Watch.Domain;
using Watch.Domain.Dash;
using Xunit;

namespace Codex.Tests.Dash;

/// <summary>
/// Post's half of Dash (P14 W-4.1.1): the wrist is shown the Mailbox as the phone shows it, and every act from it goes
/// through Post's own rules (tombstones, the undo window, group threading, the send path), so the watch never keeps a
/// second, different copy of the owner's texts (A9). Invented people and numbers throughout.
/// </summary>
public class DashRelayTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 16, 20, 0, TimeSpan.Zero);
    private static readonly TimeSpan Six = TimeSpan.FromSeconds(6);

    private const string Lee = "2125550101";
    private const string Robin = "2125550102";
    private const string Sam = "2125550103";
    private const string SamMail = "sam@example.com";
    private static readonly string GroupKey = TimelineMerger.GroupThreadKey("mms:17");

    private static readonly Dictionary<string, string> Book = new()
    {
        [Lee] = "Lee",
        [Robin] = "Robin",
        [Sam] = "Sam",
        [SamMail] = "Sam",
    };

    private static string NameOf(string key) => Book.GetValueOrDefault(key, key);

    private static TimelineItem Text(string who, int minute, string words = "hi", bool incoming = true) =>
        new(TimelineItemKind.Message, who, T0.AddMinutes(minute), words, incoming, $"sms:{who}:{minute}");

    private static TimelineItem GroupText(string who, int minute, string words = "hi", string group = "mms:17") =>
        Text(who, minute, words) with
        {
            GroupId = group,
            Participants = [Lee, Robin, Sam],
            ParticipantAddresses = ["+12125550101", "+12125550102", "+12125550103"],
        };

    private static TimelineItem Call(string who, int minute, string words = "Missed call") =>
        new(TimelineItemKind.Call, who, T0.AddMinutes(minute), words, Incoming: true, $"call:{who}:{minute}");

    // A call as the call log reads it (B7): began at T0 less <paramref name="beganAgo"/> seconds, connected for so many.
    private static TimelineItem Logged(string who, int beganAgo, int connected, int type = 1, string id = "41") =>
        CallRecordMapper.Map(id, who, T0.AddSeconds(-beganAgo).ToUnixTimeMilliseconds(), type, connected);

    private static TimelineItem Mail(string from, int minute, MailClass? kind, string subject = "Saturday plans", bool incoming = true) =>
        new(TimelineItemKind.Email, from, T0.AddMinutes(minute), "See you there.", incoming, $"mail:{from}:{minute}")
        {
            Subject = subject,
            FromAddress = from,
            MailClass = kind,
        };

    private static DashSnapshot Snapshot(
        IEnumerable<TimelineItem> items,
        Tombstones? tombstones = null,
        IReadOnlyDictionary<string, DateTimeOffset>? read = null,
        IReadOnlySet<(TimelineItemKind, string)>? hidden = null,
        SpamList? spam = null,
        DeleteBuffer? deletes = null,
        GroupNames? groups = null) =>
        new(items.ToList(),
            hidden ?? new HashSet<(TimelineItemKind, string)>(),
            spam ?? SpamList.From([]),
            deletes ?? new DeleteBuffer(),
            tombstones ?? new Tombstones(),
            PerKeyIdentityResolver.Instance,
            new ReadState(read ?? new Dictionary<string, DateTimeOffset>()),
            groups ?? GroupNames.Empty,
            NameOf);

    private static DashRelay Relay(params TimelineItem[] items) => new(Snapshot(items));

    private static string KeyOf(string threadKey) => DashIds.Correspondence(threadKey);

    private static void AssertSameNote(TimelineItem expected, TimelineItem? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal((expected.Kind, expected.ContactKey, expected.TimestampUtc, expected.Snippet, expected.Incoming, expected.SourceId),
            (actual.Kind, actual.ContactKey, actual.TimestampUtc, actual.Snippet, actual.Incoming, actual.SourceId));
    }

    // ---- dash/people ----

    [Fact]
    public void PeopleWaitingNewestFirst()
    {
        var relay = Relay(
            Text(Robin, -60, "Photos from the game are up."),
            Text(Lee, -2, "Running late. I’ll be at the field by four."),
            GroupText(Robin, -40), GroupText(Sam, -10, "Pickup moves to 3:15 on Friday."),
            Call(Sam, -30));

        var people = relay.People().People;

        Assert.Equal([KeyOf(Lee), KeyOf(GroupKey), KeyOf(Sam), KeyOf(Robin)], people.Select(person => person.Key));
        Assert.Equal(["Lee", "Lee, Robin, Sam", "Sam", "Robin"], people.Select(person => person.Name));
        Assert.Equal("Running late. I’ll be at the field by four.", people[0].Line);
        Assert.Equal("Missed call", people[2].Line);
        Assert.Equal([T0.AddMinutes(-2), T0.AddMinutes(-10), T0.AddMinutes(-30), T0.AddMinutes(-60)], people.Select(person => person.At));
        Assert.Equal([false, true, false, false], people.Select(person => person.Group));
    }

    [Fact]
    public void AGroupIsOneRow()
    {
        var relay = new DashRelay(Snapshot([GroupText(Lee, -30), GroupText(Robin, -20), GroupText(Sam, -10, "See you all at six.")],
            groups: GroupNames.Empty.Learn(GroupKey, "Team snacks")));

        var person = Assert.Single(relay.People().People);

        Assert.Equal(("Team snacks", "See you all at six.", true), (person.Name, person.Line, person.Group));
    }

    [Fact]
    public void ThePeopleAreTheMailboxsCorrespondences()
    {
        TimelineItem[] items = [Text(Lee, -5), Text(Robin, -10), Call(Sam, -15), GroupText(Robin, -20)];
        var phone = MailboxView.Build(items, new HashSet<(TimelineItemKind, string)>(), SpamList.From([]), new DeleteBuffer(),
            PerKeyIdentityResolver.Instance, new ReadState(new Dictionary<string, DateTimeOffset>()));

        var watch = Relay(items).People().People;

        Assert.Equal(phone.Waiting.Select(thread => KeyOf(thread.ContactKey)).Order(), watch.Select(person => person.Key).Order());
    }

    [Fact]
    public void NothingFiledOrTombstonedIsWaiting()
    {
        // Filed but not yet gone at the source: the tombstone stands, the message is still in the timeline.
        var filed = Text(Robin, -1);
        var tombstones = new Tombstones();
        tombstones.Mark(filed.SourceId, T0);

        var relay = new DashRelay(Snapshot([Text(Lee, -5), filed], tombstones: tombstones));

        Assert.Equal([KeyOf(Lee)], relay.People().People.Select(person => person.Key));
    }

    [Fact]
    public void WhatTheMailboxHidesIsNotWaiting()
    {
        var hidden = Text(Robin, -1);
        var spam = Text("2125550109", -2, "You have won a cruise, reply YES to claim it now");
        var pending = Text(Sam, -3);
        var deletes = new DeleteBuffer();
        deletes.Schedule(pending, T0, Six);
        var spamList = SpamList.From([]);
        spamList.Mark([spam]);

        var relay = new DashRelay(Snapshot([Text(Lee, -5), hidden, spam, pending],
            hidden: new HashSet<(TimelineItemKind, string)> { (hidden.Kind, hidden.SourceId) }, spam: spamList, deletes: deletes));

        Assert.Equal([KeyOf(Lee)], relay.People().People.Select(person => person.Key));
    }

    [Fact]
    public void MailIsNotOnPeopleWaiting()
    {
        // Mail reaches the wrist as mail lines only (A11): a person whose only waiting message is mail is not on the
        // list, and a text keeps its row's line even when a mail came after it.
        var relay = Relay(Mail(SamMail, -1, MailClass.Human), Text(Lee, -10, "On my way."), Mail(Lee, -2, MailClass.Human));

        var person = Assert.Single(relay.People().People);

        Assert.Equal((KeyOf(Lee), "On my way.", T0.AddMinutes(-10)), (person.Key, person.Line, person.At));
    }

    [Fact]
    public void UnreadIsThePhonesUnread()
    {
        var relay = new DashRelay(Snapshot([Text(Lee, -5), Text(Robin, -10)],
            read: new Dictionary<string, DateTimeOffset> { [Lee] = T0 }));

        var people = relay.People().People;

        Assert.False(people.Single(person => person.Key == KeyOf(Lee)).Unread);
        Assert.True(people.Single(person => person.Key == KeyOf(Robin)).Unread);
    }

    [Fact]
    public void APhotoSaysPhoto()
    {
        var photo = Text(Robin, -1, "") with { Attachments = [new AttachmentRef("part:7", "photo.jpg", "image/jpeg", 120_000)] };

        Assert.Equal("Photo", Assert.Single(Relay(photo).People().People).Line);
    }

    [Fact]
    public void ALineIsOneLineAndANameFitsTheRow()
    {
        var longName = new string('L', 100);
        var relay = new DashRelay(Snapshot([Text("2125550109", -1, "Running late.\nI’ll be at the field\n\nby four."), Text(Lee, -2, new string('a', 300))])
            with { NameOf = key => key == "2125550109" ? longName : NameOf(key) });

        var people = relay.People().People;

        Assert.Equal("Running late. I’ll be at the field by four.", people[0].Line);
        Assert.True(people[0].Name.Length <= DashLimits.Name);
        Assert.EndsWith("…", people[0].Name, StringComparison.Ordinal);
        Assert.True(people[1].Line.Length <= DashLimits.Line);
    }

    [Fact]
    public void AtMostTheNewestTwentyFour()
    {
        var items = Enumerable.Range(0, 30).Select(index => Text($"21255502{index:D2}", -index)).ToArray();

        var people = Relay(items).People().People;

        Assert.Equal(DashLimits.People, people.Count);
        Assert.Equal(KeyOf("2125550200"), people[0].Key);
        Assert.Equal(KeyOf("2125550223"), people[^1].Key);
    }

    [Fact]
    public void PeopleWaitingEncodes()
    {
        var people = Relay(Text(Lee, -2), GroupText(Robin, -3)).People();

        Assert.True(DashCodec.Decode(WatchIdentity.DashPeoplePath, DashCodec.Encode(people)).Accepted);
    }

    // ---- dash/thread ----

    [Fact]
    public void AThreadIsItsLastMessagesOldestFirst()
    {
        var items = Enumerable.Range(0, 20).Select(index => Text(Lee, index - 20, $"Message {index}", incoming: index % 2 == 0)).ToArray();

        var thread = Relay(items).Thread(KeyOf(Lee));

        Assert.NotNull(thread);
        Assert.Equal(("Lee", false), (thread.Title, thread.Group));
        Assert.Empty(thread.Members);
        Assert.Equal(DashLimits.Messages, thread.Messages.Count);
        Assert.Equal(items[^DashLimits.Messages..].Select(item => DashIds.Message(item.SourceId)), thread.Messages.Select(message => message.Id));
        Assert.Equal("Message 4", thread.Messages[0].Text);
        Assert.Equal([true, false], thread.Messages.Take(2).Select(message => message.Incoming));
        Assert.All(thread.Messages, message => Assert.Null(message.From));
    }

    [Fact]
    public void AGroupThreadNamesItsSendersAndMembers()
    {
        var mine = GroupText(Lee, -5, "Are you bringing the oranges?") with { Incoming = false };
        var thread = Relay(GroupText(Robin, -10, "Photo"), mine).Thread(KeyOf(GroupKey));

        Assert.NotNull(thread);
        Assert.True(thread.Group);
        Assert.Equal(["Lee", "Robin", "Sam"], thread.Members);
        Assert.Equal(["Robin", null], thread.Messages.Select(message => message.From));
    }

    [Fact]
    public void AThreadCarriesNoMail()
    {
        var thread = Relay(Text(Lee, -10), Mail(Lee, -5, MailClass.Human)).Thread(KeyOf(Lee));

        Assert.NotNull(thread);
        Assert.Equal([DashMessageType.Text], thread.Messages.Select(message => message.Type));
    }

    [Fact]
    public void ACallAVoicemailAndANoteAreEachTheirOwn()
    {
        var voicemail = new TimelineItem(TimelineItemKind.Voicemail, Lee, T0.AddMinutes(-8), "Call me back about Thursday.", true, "vm:1");
        var note = CallNote.For(Lee, "Robin can take Juniper on Thursday.", T0.AddMinutes(-1));

        var thread = Relay(Call(Lee, -10), voicemail, note).Thread(KeyOf(Lee));

        Assert.NotNull(thread);
        Assert.Equal([DashMessageType.Call, DashMessageType.Voicemail, DashMessageType.Note], thread.Messages.Select(message => message.Type));
        Assert.Equal(["Missed call", "Voicemail: Call me back about Thursday.", "Robin can take Juniper on Thursday."],
            thread.Messages.Select(message => message.Text));
    }

    [Fact]
    public void APhotoIsNamedNotSent()
    {
        var photo = Text(Robin, -1, "Photo") with
        {
            Attachments =
            [
                new AttachmentRef("part:7", "photo.jpg", "image/jpeg", 120_000, "/data/cache/photo.jpg"),
                new AttachmentRef("part:8", "clip.mp4", "video/mp4", 2_000_000),
                new AttachmentRef("part:9", "card.vcf", "text/x-vcard", 300),
            ],
        };

        var message = Assert.Single(Relay(photo).Thread(KeyOf(Robin))!.Messages);

        Assert.Equal(
            [new DashAttachment(DashIds.Attachment(photo.SourceId, "part:7"), DashAttachmentType.Photo),
             new DashAttachment(DashIds.Attachment(photo.SourceId, "part:8"), DashAttachmentType.Video),
             new DashAttachment(DashIds.Attachment(photo.SourceId, "part:9"), DashAttachmentType.File)],
            message.Attachments);
    }

    // ---- dash/photo (B5) ----

    private static TimelineItem Picture(string who, int minute, string words = "") =>
        Text(who, minute, words) with { Attachments = [new AttachmentRef("part:7", "photo.jpg", "image/jpeg", 2_400_000), new AttachmentRef("part:8", "clip.mp4", "video/mp4", 9_000_000)] };

    [Fact]
    public void APhotoIsFoundByItsId()
    {
        var picture = Picture(Robin, -1);

        var plan = Relay(Text(Lee, -3), picture).Photo(new DashPhotoAsk(DashIds.Attachment(picture.SourceId, "part:7")));

        Assert.NotNull(plan);
        Assert.Equal((picture.SourceId, "part:7"), (plan.Item.SourceId, plan.Attachment.Id));
    }

    [Fact]
    public void AVideoIsNoPhoto()
    {
        var picture = Picture(Robin, -1);

        Assert.Null(Relay(picture).Photo(new DashPhotoAsk(DashIds.Attachment(picture.SourceId, "part:8"))));
    }

    [Fact]
    public void AFiledPhotoIsNotFound()
    {
        var picture = Picture(Robin, -1);
        var tombstones = new Tombstones();
        tombstones.Mark(picture.SourceId, T0);

        Assert.Null(new DashRelay(Snapshot([picture], tombstones)).Photo(new DashPhotoAsk(DashIds.Attachment(picture.SourceId, "part:7"))));
    }

    [Fact]
    public void AMailsPictureIsNotAPhotoOnTheWrist()
    {
        var mail = Mail(SamMail, -2, MailClass.Human) with { Attachments = [new AttachmentRef("att:1", "photo.jpg", "image/jpeg", 300_000)] };

        Assert.Null(Relay(mail).Photo(new DashPhotoAsk(DashIds.Attachment(mail.SourceId, "att:1"))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Photo +1")]
    public void APhotosStandInIsNoWordsOnTheWire(string standIn)
    {
        // The wrist says "Photo" itself and draws the picture alone (W-5.1.3); the row's line still says "Photo".
        var picture = Picture(Robin, -1, standIn);

        var relay = Relay(picture);

        Assert.Equal(string.Empty, Assert.Single(relay.Thread(KeyOf(Robin))!.Messages).Text);
        Assert.Equal("Photo +1", Assert.Single(relay.People().People).Line);
    }

    [Fact]
    public void APhotosOwnWordsCross()
    {
        var picture = Picture(Robin, -1, "Look at the oranges.");

        Assert.Equal("Look at the oranges.", Assert.Single(Relay(picture).Thread(KeyOf(Robin))!.Messages).Text);
    }

    [Fact]
    public void AnUnknownKeyHasNoThread()
    {
        var relay = Relay(Text(Lee, -1), Mail(SamMail, -2, MailClass.Human));

        Assert.Null(relay.Thread(KeyOf(Robin)));
        Assert.Null(relay.Thread(KeyOf(SamMail)));
    }

    [Fact]
    public void ALongThreadIsTrimmedToFit()
    {
        // A character JSON writes as six bytes, at every limit: the newest messages that fit are kept.
        var escaped = new string('\u0001', 2 * DashLimits.MessageText);
        var items = Enumerable.Range(0, 30).Select(index => Text(Lee, index - 30, escaped) with
        {
            Attachments = Enumerable.Range(0, 12).Select(part => new AttachmentRef($"part:{part}", null, "image/png", 1)).ToList(),
        }).ToArray();

        var thread = Relay(items).Thread(KeyOf(Lee))!;
        var bytes = DashCodec.Encode(thread);

        Assert.True(bytes.Length <= DashLimits.MaxBytes, $"{bytes.Length} bytes");
        Assert.True(DashCodec.Decode(WatchIdentity.DashThreadPath, bytes).Accepted);
        Assert.Equal(DashIds.Message(items[^1].SourceId), thread.Messages[^1].Id);
        Assert.True(thread.Messages.Count < DashLimits.Messages, $"{thread.Messages.Count} messages");
        Assert.All(thread.Messages, message =>
        {
            Assert.True(message.Text.Length <= DashLimits.MessageText);
            Assert.True(message.Attachments.Count <= DashLimits.Attachments);
        });
    }

    // ---- dash/mail ----

    [Fact]
    public void MailIsPeopleOnly()
    {
        var relay = Relay(
            Mail(SamMail, -20, MailClass.Human, "Saturday plans"),
            Mail("deals@example.com", -1, MailClass.Marketing, "Half off this weekend"),
            Mail("digest@example.com", -2, MailClass.Newsletter, "This week in the garden"),
            Mail("orders@example.com", -3, MailClass.Transactional, "Your order has shipped"),
            Mail("billing@example.com", -4, MailClass.Bill, "Your statement is ready"),
            Mail("unknown@example.com", -5, null, "Not yet judged"),
            Mail(SamMail, -6, MailClass.Human, "Re: Saturday plans", incoming: false));

        var line = Assert.Single(relay.Mail().Mail);

        Assert.Equal((DashIds.Message($"mail:{SamMail}:-20"), "Sam", "Saturday plans", T0.AddMinutes(-20)), (line.Id, line.From, line.Subject, line.At));
    }

    [Fact]
    public void MailLinesAreNewestFirst()
    {
        var relay = Relay(Mail(SamMail, -30, MailClass.Human, "Older"), Mail("robin@example.com", -5, MailClass.Human, "Newer"));

        Assert.Equal(["Newer", "Older"], relay.Mail().Mail.Select(line => line.Subject));
    }

    [Fact]
    public void FiledMailIsNoLine()
    {
        var filed = Mail(SamMail, -5, MailClass.Human);
        var tombstones = new Tombstones();
        tombstones.Mark(filed.SourceId, T0);

        Assert.Empty(new DashRelay(Snapshot([filed], tombstones: tombstones)).Mail().Mail);
    }

    // ---- dash/send ----

    [Fact]
    public void ReplyGoesThroughPostsSendPath()
    {
        // The same route the phone's own reply takes: the bridge first, into the conversation the thread last spoke in,
        // and a plain text through the phone if the bridge is down or refuses it.
        var latest = Text(Lee, -2, "See you at six.") with { ExternalConversationId = "conv-lee", SourceId = "gm:7" };
        var snapshot = Snapshot([Text(Lee, -10, "Are you coming?"), latest]);
        var thread = MailboxView.Build(snapshot.Items, snapshot.Hidden, snapshot.Spam, snapshot.Deletes, snapshot.Resolver, snapshot.ReadState).Waiting.Single();
        var phone = ReplyTarget.For(thread)!;

        var plan = new DashRelay(snapshot).Send(new DashSend("send-1", KeyOf(Lee), [], "On my way."), bridgeConnected: true, canSendSms: true);

        Assert.Null(plan.Refused);
        Assert.Equal(phone.Recipients, plan.Recipients);
        Assert.Equal([Lee], plan.Recipients);
        Assert.Equal("conv-lee", plan.ConversationId);
        Assert.True(plan.TryBridge);
        Assert.Equal(TextSendPath.NativeSms, plan.Otherwise);
        Assert.Equal(TextSend.Choose(true, true) == TextSendPath.Bridge, plan.TryBridge);
        Assert.Equal(TextSend.WithoutBridge(1, true), plan.Otherwise);
    }

    [Fact]
    public void WithTheBridgeDownAPersonGetsAPlainText()
    {
        var plan = Relay(Text(Lee, -1)).Send(new DashSend("send-1", KeyOf(Lee), [], "On my way."), bridgeConnected: false, canSendSms: true);

        Assert.Null(plan.Refused);
        Assert.False(plan.TryBridge);
        Assert.Equal(TextSendPath.NativeSms, plan.Otherwise);
    }

    [Fact]
    public void AGroupReplyIsOneMmsToEveryone()
    {
        var plan = Relay(GroupText(Robin, -3), GroupText(Sam, -1))
            .Send(new DashSend("send-1", KeyOf(GroupKey), [], "Reply to all."), bridgeConnected: false, canSendSms: true);

        Assert.Null(plan.Refused);
        Assert.Equal(["+12125550101", "+12125550102", "+12125550103"], plan.Recipients);
        Assert.Equal(TextSendPath.GroupMms, plan.Otherwise);
    }

    [Fact]
    public void AGroupKnownOnlyFromNotificationsIsNotAnswered()
    {
        // The phone offers it no reply either: a notification names only who has spoken.
        var heard = GroupText(Robin, -1, group: MessagingNoticeMapper.GroupPrefix + "team");

        var plan = Relay(heard).Send(new DashSend("send-1", KeyOf(TimelineMerger.GroupThreadKey(heard.GroupId!)), [], "Hi"), true, true);

        Assert.Equal(DashSendOutcome.Nowhere, plan.Refused);
        Assert.Empty(plan.Recipients);
    }

    [Fact]
    public void NothingCanCarryItSaysSo()
    {
        var plan = Relay(Text(Lee, -1)).Send(new DashSend("send-1", KeyOf(Lee), [], "Hi"), bridgeConnected: false, canSendSms: false);

        Assert.Equal(DashSendOutcome.NoRoute, plan.Refused);
    }

    [Fact]
    public void WithOnlyTheBridgeItIsTried()
    {
        var plan = Relay(Text(Lee, -1)).Send(new DashSend("send-1", KeyOf(Lee), [], "Hi"), bridgeConnected: true, canSendSms: false);

        Assert.Null(plan.Refused);
        Assert.True(plan.TryBridge);
        Assert.Equal(TextSendPath.Unavailable, plan.Otherwise);
    }

    [Fact]
    public void ANewTextGoesToItsNumbers()
    {
        var plan = Relay().Send(new DashSend("send-1", null, ["+1 212-555-0104", "(212) 555-0105"], "On my way."), true, true);

        Assert.Null(plan.Refused);
        Assert.Equal(["+1 212-555-0104", "(212) 555-0105"], plan.Recipients);
        Assert.Null(plan.ConversationId);
        Assert.Equal(TextSendPath.GroupMms, plan.Otherwise);
    }

    [Theory]
    [InlineData("Lee")]
    [InlineData("lee@example.com")]
    public void ANumberThatIsNoNumberIsNowhere(string to)
    {
        var plan = Relay().Send(new DashSend("send-1", null, [to], "Hi"), true, true);

        Assert.Equal(DashSendOutcome.Nowhere, plan.Refused);
    }

    [Fact]
    public void AReplyWithNobodyToReachIsNowhere()
    {
        var relay = Relay(Text(Lee, -1), Mail(SamMail, -2, MailClass.Human));

        Assert.Equal(DashSendOutcome.Nowhere, relay.Send(new DashSend("send-1", KeyOf(Robin), [], "Hi"), true, true).Refused);
        Assert.Equal(DashSendOutcome.Nowhere, relay.Send(new DashSend("send-2", KeyOf(SamMail), [], "Hi"), true, true).Refused);
    }

    // ---- dash/file ----

    [Fact]
    public void FileKeepsTheTombstoneRule()
    {
        TimelineItem[] items = [Text(Lee, -10), Call(Lee, -5), Mail(Lee, -3, MailClass.Human), Text(Robin, -1)];
        var acts = new DashActs();

        var plan = Relay(items).File(new DashFile("act-1", KeyOf(Lee), [], Six), T0, Six, acts);

        // What the row on the wrist showed: Lee's texts and calls, not the mail the wrist only saw as a line.
        Assert.Equal(DashOutcome.Filed, plan.Outcome);
        Assert.Equal([items[0].SourceId, items[1].SourceId], plan.Items.Select(item => item.SourceId));
        Assert.Equal(DashRelay.HoldFor(Six, Six), plan.Window);
        Assert.Equal(SyncMoment.Delay(PostAction.Filed, plan.Window), plan.SyncAfter);

        // Post files each as the phone does (Filing.File raises the tombstone). The source still has them: nothing has
        // been deleted there yet, and reconcile must not bring them back.
        var tombstones = new Tombstones();
        foreach (var item in plan.Items)
        {
            tombstones.Mark(item.SourceId, T0);
        }
        var after = new DashRelay(Snapshot(items, tombstones: tombstones));

        Assert.Equal([KeyOf(Robin)], after.People().People.Select(person => person.Key));
        Assert.Equal([items[2].SourceId, items[3].SourceId], tombstones.Reconcile(items.Select(item => item.SourceId)));
        Assert.Equal(plan.Items.Select(item => item.SourceId).Order(StringComparer.Ordinal), tombstones.Outstanding());
    }

    [Fact]
    public void FilingMessagesTakesOnlyThose()
    {
        TimelineItem[] items = [Text(Lee, -10), Text(Lee, -5), Mail(SamMail, -3, MailClass.Human)];

        var plan = Relay(items).File(new DashFile("act-1", null, [DashIds.Message(items[1].SourceId), DashIds.Message(items[2].SourceId)], Six),
            T0, Six, new DashActs());

        Assert.Equal(DashOutcome.Filed, plan.Outcome);
        Assert.Equal(new[] { items[1].SourceId, items[2].SourceId }.Order(StringComparer.Ordinal), plan.Items.Select(item => item.SourceId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FilingWhatPostDoesNotHaveIsUnknown()
    {
        var plan = Relay(Text(Lee, -1)).File(new DashFile("act-1", KeyOf(Robin), [], Six), T0, Six, new DashActs());

        Assert.Equal(DashOutcome.Unknown, plan.Outcome);
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void TheSameActTwiceIsDoneOnce()
    {
        var acts = new DashActs();
        var relay = Relay(Text(Lee, -1));

        relay.File(new DashFile("act-1", KeyOf(Lee), [], Six), T0, Six, acts);
        var again = relay.File(new DashFile("act-1", KeyOf(Lee), [], Six), T0.AddSeconds(1), Six, acts);

        Assert.Equal(DashOutcome.Again, again.Outcome);
        Assert.Empty(again.Items);
    }

    [Fact]
    public void AnUndoInsideTheWindowTakesTheFilingBack()
    {
        var acts = new DashActs();
        var plan = Relay(Text(Lee, -10), Text(Lee, -5)).File(new DashFile("act-1", KeyOf(Lee), [], Six), T0, Six, acts);

        var undo = DashRelay.Undo(new DashUndo("act-1"), T0 + plan.Window, acts);

        Assert.Equal(DashOutcome.TakenBack, undo.Outcome);
        Assert.Equal(DashActKind.File, undo.Kind);
        Assert.Equal(plan.Items.Select(item => item.SourceId), undo.Items.Select(item => item.SourceId));
    }

    [Fact]
    public void AnUndoAfterTheWindowIsTooLate()
    {
        var acts = new DashActs();
        var plan = Relay(Text(Lee, -1)).File(new DashFile("act-1", KeyOf(Lee), [], Six), T0, Six, acts);

        var undo = DashRelay.Undo(new DashUndo("act-1"), T0 + plan.Window + TimeSpan.FromMilliseconds(1), acts);

        Assert.Equal(DashOutcome.TooLate, undo.Outcome);
        Assert.Empty(undo.Items);
    }

    [Fact]
    public void AnUndoOfNothingIsUnknown()
    {
        Assert.Equal(DashOutcome.Unknown, DashRelay.Undo(new DashUndo("act-9"), T0, new DashActs()).Outcome);
    }

    // ---- dash/delete ----

    [Fact]
    public void ADeleteIsHeldForTheUndoWindow()
    {
        TimelineItem[] items = [Text(Lee, -10), Text(Robin, -5)];
        var acts = new DashActs();

        var plan = Relay(items).Delete(new DashDelete("act-1", KeyOf(Lee), [], TimeSpan.FromSeconds(30)), T0, Six, acts);

        Assert.Equal(DashOutcome.Held, plan.Outcome);
        Assert.Equal([items[0].SourceId], plan.Items.Select(item => item.SourceId));
        Assert.Equal(DashRelay.HoldFor(TimeSpan.FromSeconds(30), Six), plan.Window);
        Assert.Equal(SyncMoment.Delay(PostAction.Deleted, plan.Window), plan.SyncAfter);

        // Post holds it in the delete buffer the Mailbox uses: gone from both views at once, committed when the window ends.
        var deletes = new DeleteBuffer();
        foreach (var item in plan.Items)
        {
            deletes.Schedule(item, T0, plan.Window);
        }

        Assert.Equal([KeyOf(Robin)], new DashRelay(Snapshot(items, deletes: deletes)).People().People.Select(person => person.Key));
        Assert.Empty(deletes.TakeDue(T0 + plan.Window - TimeSpan.FromMilliseconds(1)));
        Assert.Single(deletes.TakeDue(T0 + plan.Window));
    }

    [Fact]
    public void AnUndoTakesTheDeleteBack()
    {
        var acts = new DashActs();
        var deletes = new DeleteBuffer();
        var plan = Relay(Text(Lee, -10), Call(Lee, -5)).Delete(new DashDelete("act-1", KeyOf(Lee), [], Six), T0, Six, acts);
        foreach (var item in plan.Items)
        {
            deletes.Schedule(item, T0, plan.Window);
        }

        var undo = DashRelay.Undo(new DashUndo("act-1"), T0.AddSeconds(5), acts);

        Assert.Equal((DashOutcome.TakenBack, DashActKind.Delete), (undo.Outcome, undo.Kind));
        Assert.All(undo.Items, item => Assert.True(deletes.Undo(item.Kind, item.SourceId)));
        Assert.Empty(deletes.Pending);
    }

    [Fact]
    public void TheWatchsUndoUsesTheSameWindow()
    {
        // The wrist's ring and the phone's dog-ear both follow the suite's one window (FoldTiming); Post holds a wrist's
        // act for the longer of the two, and a moment more for the message to cross.
        Assert.Equal(TimeSpan.FromSeconds(10), DashRelay.TransportGrace);
        Assert.Equal(Six + DashRelay.TransportGrace, DashRelay.HoldFor(Six, Six));
        Assert.Equal(TimeSpan.FromSeconds(30) + DashRelay.TransportGrace, DashRelay.HoldFor(TimeSpan.FromSeconds(30), Six));
        Assert.Equal(TimeSpan.FromSeconds(30) + DashRelay.TransportGrace, DashRelay.HoldFor(Six, TimeSpan.FromSeconds(30)));
        Assert.Equal(FoldTiming.WindowFor(TimeSpan.Zero) + DashRelay.TransportGrace, DashRelay.HoldFor(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        Assert.Equal(DashLimits.MaxWindow + DashRelay.TransportGrace, DashRelay.HoldFor(TimeSpan.FromHours(1), Six));
        Assert.True(DashRelay.HoldFor(FoldTiming.UndoWindow, FoldTiming.UndoWindow) >= FoldTiming.UndoWindow);
    }

    // ---- dash/read ----

    [Fact]
    public void ReadOnTheWatchIsReadOnThePhone()
    {
        TimelineItem[] items = [Text(Lee, -10), Text(Lee, -5)];
        var before = new DashRelay(Snapshot(items));
        Assert.True(before.People().People.Single().Unread);

        var plan = before.Read(new DashRead(KeyOf(Lee), items[1].TimestampUtc));

        Assert.Equal((DashOutcome.Marked, Lee, items[1].TimestampUtc), (plan.Outcome, plan.ThreadKey, plan.Stamp));

        // Post stamps the read store the Mailbox reads (ReadStateStore.MarkRead): read on the phone and on the wrist.
        var read = new Dictionary<string, DateTimeOffset> { [plan.ThreadKey!] = plan.Stamp };
        var phone = MailboxView.Build(items, new HashSet<(TimelineItemKind, string)>(), SpamList.From([]), new DeleteBuffer(),
            PerKeyIdentityResolver.Instance, new ReadState(read));
        Assert.False(phone.Waiting.Single().HasUnread);
        Assert.False(new DashRelay(Snapshot(items, read: read)).People().People.Single().Unread);
    }

    [Fact]
    public void ALaterTextStaysUnread()
    {
        TimelineItem[] items = [Text(Lee, -10), Text(Lee, -5), Text(Lee, -1, "One more thing.")];

        var plan = Relay(items).Read(new DashRead(KeyOf(Lee), items[1].TimestampUtc));
        var read = new Dictionary<string, DateTimeOffset> { [Lee] = plan.Stamp };

        Assert.True(new DashRelay(Snapshot(items, read: read)).People().People.Single().Unread);
    }

    [Fact]
    public void AReadNeverMovesBack()
    {
        TimelineItem[] items = [Text(Lee, -10), Text(Lee, -5)];
        var relay = new DashRelay(Snapshot(items, read: new Dictionary<string, DateTimeOffset> { [Lee] = T0 }));

        Assert.Equal(T0, relay.Read(new DashRead(KeyOf(Lee), items[0].TimestampUtc)).Stamp);
    }

    [Fact]
    public void AReadIsToTheMillisecondTheWireCarries()
    {
        // The wire carries whole milliseconds; a message stamped finer is still the one the wrist showed.
        var fine = Text(Lee, -5) with { TimestampUtc = T0.AddMinutes(-5).AddTicks(4321) };
        var shown = DateTimeOffset.FromUnixTimeMilliseconds(fine.TimestampUtc.ToUnixTimeMilliseconds());

        var plan = Relay(fine).Read(new DashRead(KeyOf(Lee), shown));

        Assert.Equal(fine.TimestampUtc, plan.Stamp);
    }

    [Fact]
    public void AnUnknownReadIsUnknown()
    {
        var relay = Relay(Text(Lee, -5));

        Assert.Equal(DashOutcome.Unknown, relay.Read(new DashRead(KeyOf(Robin), T0)).Outcome);
        Assert.Equal(DashOutcome.Unknown, relay.Read(new DashRead(KeyOf(Lee), T0.AddHours(-1))).Outcome);
    }

    // ---- dash/call-note ----

    [Fact]
    public void ACallNoteIsKeptWherePostKeepsThem()
    {
        var call = Call(Lee, -6, "Incoming call");

        var plan = Relay(call).CallNote(new DashCallNote(DashIds.Message(call.SourceId), null, " Robin can take Juniper on Thursday. "), T0);

        Assert.Equal(DashOutcome.Kept, plan.Outcome);
        AssertSameNote(CallNote.For(Lee, "Robin can take Juniper on Thursday.", T0), plan.Note);
    }

    [Fact]
    public void ANoteOnTheWatchsOwnCallGoesByItsNumber()
    {
        var plan = Relay().CallNote(new DashCallNote(null, "+1 212-555-0103", "Call back about the dentist."), T0);

        Assert.Equal(DashOutcome.Kept, plan.Outcome);
        AssertSameNote(CallNote.For("+1 212-555-0103", "Call back about the dentist.", T0), plan.Note);
        Assert.Equal(Sam, plan.Note!.ContactKey);
    }

    [Fact]
    public void ANoteOnNoCallIsUnknown()
    {
        var text = Text(Lee, -1);
        var relay = Relay(text);

        Assert.Equal(DashOutcome.Unknown, relay.CallNote(new DashCallNote(DashIds.Message("call:none"), null, "Hi"), T0).Outcome);
        Assert.Equal(DashOutcome.Unknown, relay.CallNote(new DashCallNote(DashIds.Message(text.SourceId), null, "Hi"), T0).Outcome);
        Assert.Equal(DashOutcome.Unknown, relay.CallNote(new DashCallNote(null, "Lee", "Hi"), T0).Outcome);
        Assert.Null(relay.CallNote(new DashCallNote(null, "Lee", "Hi"), T0).Note);
    }

    // ---- the acts' book ----

    [Fact]
    public void TheBookKeepsTheNewest()
    {
        var acts = new DashActs();
        for (var index = 0; index < DashActs.Kept + 6; index++)
        {
            acts.Record(new DashActRecord($"act-{index}", DashActKind.File, [], T0));
        }

        Assert.Equal(64, DashActs.Kept);
        Assert.Equal(DashActs.Kept, acts.All.Count);
        Assert.False(acts.Knows("act-0"));
        Assert.True(acts.Knows($"act-{DashActs.Kept + 5}"));
    }

    [Fact]
    public void AnUndoTwiceIsAgain()
    {
        var acts = new DashActs();
        acts.Record(new DashActRecord("act-1", DashActKind.Delete, [new ActedItem(TimelineItemKind.Message, "sms:1")], T0.AddSeconds(16)));

        Assert.Equal(DashOutcome.TakenBack, acts.Undo("act-1", T0).Outcome);
        Assert.Equal(DashOutcome.Again, acts.Undo("act-1", T0).Outcome);
        Assert.True(acts.Knows("act-1"));
    }

    [Fact]
    public void TheBookComesBackFromItsRecords()
    {
        var acts = new DashActs();
        acts.Record(new DashActRecord("act-1", DashActKind.File, [new ActedItem(TimelineItemKind.Call, "call:1")], T0));

        var again = DashActs.From(acts.All);

        Assert.True(again.Knows("act-1"));
        Assert.Equal(acts.All.Single().Items, again.All.Single().Items);
    }

    // ---- B7: the call the wrist heard end, and its note (W-5.1.5) ----

    [Fact]
    public void TheCallThatJustEndedIsNamed()
    {
        // Rang ten seconds, talked six minutes, ended at T0, when the wrist heard its own call state go idle.
        var call = Logged(Lee, beganAgo: 370, connected: 360);

        var answer = Relay(call).CallAt(new DashCallAsk(T0));

        Assert.Equal(new DashCall(T0, DashIds.Message(call.SourceId), "Lee", TimeSpan.FromMinutes(6)), answer);
    }

    [Fact]
    public void ACallNobodyWasOnIsNoCall()
    {
        Assert.Null(Relay(Logged(Lee, beganAgo: 30, connected: 0, type: 3)).CallAt(new DashCallAsk(T0)).Call);
        Assert.Null(Relay(Logged(Lee, beganAgo: 30, connected: 0, type: 2)).CallAt(new DashCallAsk(T0)).Call);
    }

    [Fact]
    public void ACallLongAgoIsNoCall()
    {
        var answer = Relay(Logged(Lee, beganAgo: 3600, connected: 360)).CallAt(new DashCallAsk(T0));

        Assert.Equal(new DashCall(T0, null, null, null), answer);
    }

    [Fact]
    public void TheNearestCallIsTheOne()
    {
        var earlier = Logged(Robin, beganAgo: 150, connected: 60, id: "40");
        var nearer = Logged(Lee, beganAgo: 65, connected: 60, id: "41");

        Assert.Equal(DashIds.Message(nearer.SourceId), Relay(earlier, nearer).CallAt(new DashCallAsk(T0)).Call);
        Assert.Equal(DashIds.Message(earlier.SourceId), Relay(earlier, nearer).CallAt(new DashCallAsk(T0.AddSeconds(-85))).Call);
    }

    [Fact]
    public void ACallerTheBookCannotNameHasNoName()
    {
        // The book names an unknown number by the number itself; the wrist is told no name rather than a number.
        var stranger = Logged("2125550199", beganAgo: 70, connected: 60);

        var answer = Relay(stranger).CallAt(new DashCallAsk(T0));

        Assert.Equal(DashIds.Message(stranger.SourceId), answer.Call);
        Assert.Null(answer.Name);
        Assert.DoesNotContain("0199", System.Text.Encoding.UTF8.GetString(DashCodec.Encode(answer)), StringComparison.Ordinal);
    }

    [Fact]
    public void ACallAnsweredOnTheWatchIsTheCall()
    {
        // Rung on the phone and taken on the wrist: the phone's log knows the call, if not how long it lasted.
        var elsewhere = Logged(Lee, beganAgo: 200, connected: 0, type: 7);

        var answer = Relay(elsewhere).CallAt(new DashCallAsk(T0.AddSeconds(-170)));

        Assert.Equal((DashIds.Message(elsewhere.SourceId), (TimeSpan?)TimeSpan.Zero), (answer.Call, answer.Length));
    }

    [Fact]
    public void ANoteIsNeverTheCall()
    {
        var note = CallNote.For(Lee, "Robin can take Juniper on Thursday.", T0.AddSeconds(-5));

        Assert.Null(Relay(note).CallAt(new DashCallAsk(T0)).Call);
    }

    [Fact]
    public void ANoteToldTwiceIsKeptOnce()
    {
        // The note carries the moment it was written on the wrist, which names it, so a second telling replaces the first.
        var call = Logged(Lee, beganAgo: 370, connected: 360);
        var note = new DashCallNote(DashIds.Message(call.SourceId), null, "Pick up at four.") { At = T0.AddMinutes(1) };

        var first = Relay(call).CallNote(note, T0.AddMinutes(2)).Note!;
        var again = Relay(call).CallNote(note, T0.AddMinutes(5)).Note!;

        Assert.Equal((first.SourceId, first.TimestampUtc), (again.SourceId, again.TimestampUtc));
        Assert.Equal(T0.AddMinutes(1), first.TimestampUtc);
    }
}
