using System.Text;
using System.Text.Json;
using Watch.Domain;
using Watch.Domain.Dash;
using Watch.Domain.Updates;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// Dash's contracts (plan §2.5, W-4.1.1): every message carries an integer <c>v</c>, a newer one is refused rather than
/// guessed at, and anything malformed refuses the whole message. Each is read for the path it arrived on.
/// </summary>
public class DashCodecTests
{
    private static ContractRead<DashMessage> Decode(string path, string json) => DashCodec.Decode(path, Encoding.UTF8.GetBytes(json));

    [Theory]
    [MemberData(nameof(DashFixtures.Names), MemberType = typeof(DashFixtures))]
    public void EveryMessageRoundTrips(string name)
    {
        var sent = Messages[name];
        var bytes = DashCodec.Encode(sent);

        var read = DashCodec.Decode(sent.Path, bytes);

        Assert.True(read.Accepted, $"{name}: {read.Refusal}");
        Assert.Equal(DashCodec.Version, read.Version);
        Assert.Equal(sent.GetType(), read.Message!.GetType());
        Assert.Equal(bytes, DashCodec.Encode(read.Message));
    }

    [Fact]
    public void PeopleKeepEveryField()
    {
        var read = Assert.IsType<DashPeople>(DashCodec.Decode(WatchIdentity.DashPeoplePath, DashCodec.Encode(People)).Message);

        Assert.Equal(People.People, read.People);
    }

    [Fact]
    public void AThreadKeepsEveryField()
    {
        var read = Assert.IsType<DashThread>(DashCodec.Decode(WatchIdentity.DashThreadPath, DashCodec.Encode(GroupThread)).Message);

        Assert.Equal((GroupThread.Key, GroupThread.Title, GroupThread.Group), (read.Key, read.Title, read.Group));
        Assert.Equal(GroupThread.Members, read.Members);
        Assert.Equal(GroupThread.Messages.Count, read.Messages.Count);
        for (var index = 0; index < GroupThread.Messages.Count; index++)
        {
            var (expected, actual) = (GroupThread.Messages[index], read.Messages[index]);
            Assert.Equal((expected.Id, expected.At, expected.Incoming, expected.Type, expected.Text, expected.From),
                (actual.Id, actual.At, actual.Incoming, actual.Type, actual.Text, actual.From));
            Assert.Equal(expected.Attachments, actual.Attachments);
        }
    }

    [Fact]
    public void ActsKeepTheirTargetAndWindow()
    {
        var file = Assert.IsType<DashFile>(DashCodec.Decode(WatchIdentity.DashFilePath, DashCodec.Encode(Messages["file messages"])).Message);
        var send = Assert.IsType<DashSend>(DashCodec.Decode(WatchIdentity.DashSendPath, DashCodec.Encode(Messages["new text"])).Message);
        var note = Assert.IsType<DashCallNote>(DashCodec.Decode(WatchIdentity.DashCallNotePath, DashCodec.Encode(Messages["note on a call"])).Message);

        Assert.Equal(("act-2", (string?)null, TimeSpan.FromSeconds(30)), (file.Id, file.Key, file.Window));
        Assert.Equal([TextId, CallId], file.Messages);
        Assert.Equal(["+1 212-555-0101", "+12125550102"], send.To);
        Assert.Null(send.Key);
        Assert.Equal((CallId, (string?)null, "Robin can take Juniper on Thursday."), (note.Call, note.Number, note.Text));
    }

    [Theory]
    [MemberData(nameof(DashFixtures.Names), MemberType = typeof(DashFixtures))]
    public void EveryMessageCarriesAnIntegerV(string name)
    {
        using var document = JsonDocument.Parse(DashCodec.Encode(Messages[name]));
        var v = document.RootElement.GetProperty("v");

        Assert.Equal(JsonValueKind.Number, v.ValueKind);
        Assert.Equal(1, v.GetInt32());
        Assert.Equal(1, DashCodec.Version);
    }

    [Fact]
    public void AnInstantIsMilliseconds()
    {
        // The watch says "5 min ago" itself: the wire carries when, never the words.
        using var document = JsonDocument.Parse(DashCodec.Encode(People));
        var at = document.RootElement.GetProperty("people")[0].GetProperty("at");

        Assert.Equal(JsonValueKind.Number, at.ValueKind);
        Assert.Equal(T0.ToUnixTimeMilliseconds(), at.GetInt64());
    }

    [Fact]
    public void PeopleCarryNoPhoto()
    {
        // A person is their name and one line (WD §11); a photo comes on request only.
        using var document = JsonDocument.Parse(DashCodec.Encode(People));
        var root = document.RootElement;

        Assert.Equal(["v", "kind", "people"], root.EnumerateObject().Select(property => property.Name));
        Assert.All(root.GetProperty("people").EnumerateArray(), person =>
            Assert.Equal(["key", "name", "line", "at", "unread", "group"], person.EnumerateObject().Select(property => property.Name)));
    }

    [Fact]
    public void WordsAreWrittenAsThemselves()
    {
        // Not ’: a message stays inside its budget with every character a phone can type.
        var bytes = DashCodec.Encode(new DashPeople([new DashPerson(LeeKey, "Juniper’s school", "Pickup moves to 3:15 on Friday.", T0, false, false)]));

        Assert.Contains("Juniper’s school", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void ANewerVersionIsRefused()
    {
        var read = Decode(WatchIdentity.DashPeoplePath, "{\"v\":2,\"kind\":\"ask\"}");

        Assert.False(read.Accepted);
        Assert.Equal(ContractRefusal.Newer, read.Refusal);
        Assert.Equal(2, read.Version);
        Assert.Null(read.Message);
    }

    [Fact]
    public void ANewerVersionIsRefusedBeforeItsKindIsRead()
    {
        // A newer Dash may mean something else by the same words, so nothing past v is trusted.
        var read = Decode(WatchIdentity.DashSendPath, "{\"v\":3,\"kind\":\"speak\",\"words\":7}");

        Assert.Equal(ContractRefusal.Newer, read.Refusal);
        Assert.Equal(3, read.Version);
    }

    public static TheoryData<string, string> Malformed() => new()
    {
        // The envelope.
        { "/dash/people", "{\"kind\":\"ask\"}" },
        { "/dash/people", "{\"v\":\"1\",\"kind\":\"ask\"}" },
        { "/dash/people", "{\"v\":1.5,\"kind\":\"ask\"}" },
        { "/dash/people", "{\"v\":0,\"kind\":\"ask\"}" },
        { "/dash/people", "{\"v\":1}" },
        { "/dash/people", "{\"v\":1,\"kind\":\"tell\"}" },
        { "/dash/people", "[]" },
        { "/dash/people", "not json" },
        // People.
        { "/dash/people", "{\"v\":1,\"kind\":\"people\"}" },
        { "/dash/people", "{\"v\":1,\"kind\":\"people\",\"people\":[{\"name\":\"Lee\",\"line\":\"Hi\",\"at\":1,\"unread\":true,\"group\":false}]}" },
        { "/dash/people", "{\"v\":1,\"kind\":\"people\",\"people\":[{\"key\":\"c0123\",\"name\":\"Lee\",\"line\":\"Hi\",\"at\":1,\"unread\":true,\"group\":false}]}" },
        { "/dash/people", "{\"v\":1,\"kind\":\"people\",\"people\":[{\"key\":\"m0123456789abcdef\",\"name\":\"Lee\",\"line\":\"Hi\",\"at\":1,\"unread\":true,\"group\":false}]}" },
        { "/dash/people", "{\"v\":1,\"kind\":\"people\",\"people\":[{\"key\":\"c0123456789abcdef\",\"name\":\"\",\"line\":\"Hi\",\"at\":1,\"unread\":true,\"group\":false}]}" },
        { "/dash/people", "{\"v\":1,\"kind\":\"people\",\"people\":[{\"key\":\"c0123456789abcdef\",\"name\":\"Lee\",\"line\":\"Hi\",\"at\":1,\"unread\":\"yes\",\"group\":false}]}" },
        { "/dash/people", "{\"v\":1,\"kind\":\"people\",\"people\":[{\"key\":\"c0123456789abcdef\",\"name\":\"Lee\",\"line\":\"Hi\",\"at\":\"2 min\",\"unread\":true,\"group\":false}]}" },
        { "/dash/people", "{\"v\":1,\"kind\":\"people\",\"people\":[{\"key\":\"c0123456789abcdef\",\"name\":\"Lee\",\"line\":\"Hi\",\"at\":-1,\"unread\":true,\"group\":false}]}" },
        { "/dash/people", "{\"v\":1,\"kind\":\"people\",\"people\":[{\"key\":\"c0123456789abcdef\",\"name\":\"Lee\",\"line\":\"Hi\",\"at\":1.5,\"unread\":true,\"group\":false}]}" },
        { "/dash/people", "{\"v\":1,\"kind\":\"people\",\"people\":[{\"key\":\"c0123456789abcdef\",\"name\":\"Lee\",\"line\":\"Hi\",\"at\":1,\"unread\":true}]}" },
        // A conversation.
        { "/dash/thread", "{\"v\":1,\"kind\":\"ask\"}" },
        { "/dash/thread", "{\"v\":1,\"kind\":\"ask\",\"key\":\"C0123456789ABCDEF\"}" },
        { "/dash/thread", "{\"v\":1,\"kind\":\"thread\",\"key\":\"c0123456789abcdef\",\"title\":\"Lee\",\"group\":false,\"members\":[],\"messages\":[{\"id\":\"m0123456789abcdef\",\"at\":1,\"incoming\":true,\"type\":\"fax\",\"text\":\"Hi\",\"attachments\":[]}]}" },
        { "/dash/thread", "{\"v\":1,\"kind\":\"thread\",\"key\":\"c0123456789abcdef\",\"title\":\"Lee\",\"group\":false,\"members\":[],\"messages\":[{\"id\":\"c0123456789abcdef\",\"at\":1,\"incoming\":true,\"type\":\"text\",\"text\":\"Hi\",\"attachments\":[]}]}" },
        { "/dash/thread", "{\"v\":1,\"kind\":\"thread\",\"key\":\"c0123456789abcdef\",\"title\":\"Lee\",\"group\":false,\"members\":[],\"messages\":[{\"id\":\"m0123456789abcdef\",\"at\":1,\"incoming\":true,\"type\":\"text\",\"text\":\"Hi\",\"attachments\":[{\"id\":\"a0123456789abcdef\",\"type\":\"gif\"}]}]}" },
        { "/dash/thread", "{\"v\":1,\"kind\":\"thread\",\"key\":\"c0123456789abcdef\",\"title\":\"Lee\",\"group\":false,\"members\":[],\"messages\":[{\"id\":\"m0123456789abcdef\",\"at\":1,\"incoming\":true,\"type\":\"text\",\"text\":\"Hi\",\"attachments\":[{\"id\":\"m0123456789abcdef\",\"type\":\"photo\"}]}]}" },
        { "/dash/thread", "{\"v\":1,\"kind\":\"thread\",\"key\":\"c0123456789abcdef\",\"title\":\"Lee\",\"group\":false,\"members\":[\"\"],\"messages\":[]}" },
        { "/dash/thread", "{\"v\":1,\"kind\":\"thread\",\"key\":\"c0123456789abcdef\",\"title\":\"Lee\",\"group\":false,\"members\":[],\"messages\":[{\"id\":\"m0123456789abcdef\",\"at\":1,\"incoming\":true,\"type\":\"text\",\"text\":\"Hi\",\"from\":7,\"attachments\":[]}]}" },
        // A send.
        { "/dash/send", "{\"v\":1,\"kind\":\"send\",\"id\":\"send-1\",\"key\":\"c0123456789abcdef\",\"to\":[\"+12125550101\"],\"text\":\"Hi\"}" },
        { "/dash/send", "{\"v\":1,\"kind\":\"send\",\"id\":\"send-1\",\"text\":\"Hi\"}" },
        { "/dash/send", "{\"v\":1,\"kind\":\"send\",\"id\":\"send-1\",\"key\":\"c0123456789abcdef\",\"text\":\"  \"}" },
        { "/dash/send", "{\"v\":1,\"kind\":\"send\",\"id\":\"send-1\",\"to\":[\"\"],\"text\":\"Hi\"}" },
        { "/dash/send", "{\"v\":1,\"kind\":\"send\",\"id\":\"Send_1\",\"key\":\"c0123456789abcdef\",\"text\":\"Hi\"}" },
        { "/dash/send", "{\"v\":1,\"kind\":\"send\",\"key\":\"c0123456789abcdef\",\"text\":\"Hi\"}" },
        { "/dash/send", "{\"v\":1,\"kind\":\"result\",\"id\":\"send-1\",\"outcome\":\"lost\"}" },
        { "/dash/send", "{\"v\":1,\"kind\":\"result\",\"id\":\"send-1\"}" },
        // File, delete and undo.
        { "/dash/file", "{\"v\":1,\"kind\":\"file\",\"id\":\"act-1\",\"key\":\"c0123456789abcdef\",\"window\":0}" },
        { "/dash/file", "{\"v\":1,\"kind\":\"file\",\"id\":\"act-1\",\"key\":\"c0123456789abcdef\",\"window\":120001}" },
        { "/dash/file", "{\"v\":1,\"kind\":\"file\",\"id\":\"act-1\",\"key\":\"c0123456789abcdef\"}" },
        { "/dash/file", "{\"v\":1,\"kind\":\"file\",\"id\":\"act-1\",\"key\":\"c0123456789abcdef\",\"ids\":[\"m0123456789abcdef\"],\"window\":6000}" },
        { "/dash/file", "{\"v\":1,\"kind\":\"file\",\"id\":\"act-1\",\"window\":6000}" },
        { "/dash/file", "{\"v\":1,\"kind\":\"file\",\"id\":\"act-1\",\"ids\":[],\"window\":6000}" },
        { "/dash/file", "{\"v\":1,\"kind\":\"file\",\"id\":\"act-1\",\"ids\":[\"sms:12\"],\"window\":6000}" },
        { "/dash/delete", "{\"v\":1,\"kind\":\"delete\",\"key\":\"c0123456789abcdef\",\"window\":6000}" },
        { "/dash/delete", "{\"v\":1,\"kind\":\"delete\",\"id\":\"act-1\",\"key\":\"c0123456789abcdef\",\"window\":\"6s\"}" },
        { "/dash/undo", "{\"v\":1,\"kind\":\"undo\"}" },
        { "/dash/undo", "{\"v\":1,\"kind\":\"undo\",\"act\":\"\"}" },
        // Read.
        { "/dash/read", "{\"v\":1,\"kind\":\"read\",\"key\":\"c0123456789abcdef\"}" },
        { "/dash/read", "{\"v\":1,\"kind\":\"read\",\"through\":1}" },
        // A call note.
        { "/dash/call-note", "{\"v\":1,\"kind\":\"note\",\"call\":\"m0123456789abcdef\",\"number\":\"+12125550101\",\"text\":\"Hi\"}" },
        { "/dash/call-note", "{\"v\":1,\"kind\":\"note\",\"text\":\"Hi\"}" },
        { "/dash/call-note", "{\"v\":1,\"kind\":\"note\",\"call\":\"m0123456789abcdef\",\"text\":\"\"}" },
        { "/dash/call-note", "{\"v\":1,\"kind\":\"note\",\"call\":\"c0123456789abcdef\",\"text\":\"Hi\"}" },
        // Mail.
        { "/dash/mail", "{\"v\":1,\"kind\":\"mail\"}" },
        { "/dash/mail", "{\"v\":1,\"kind\":\"mail\",\"mail\":[{\"id\":\"m0123456789abcdef\",\"from\":\"\",\"subject\":\"Plans\",\"at\":1}]}" },
        { "/dash/mail", "{\"v\":1,\"kind\":\"mail\",\"mail\":[{\"id\":\"m0123456789abcdef\",\"from\":\"Sam\",\"at\":1}]}" },
        { "/dash/mail", "{\"v\":1,\"kind\":\"mail\",\"mail\":[{\"id\":\"m0123456789abcdef\",\"from\":\"Sam\",\"subject\":\"Plans\"}]}" },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedMessageIsUnreadable(string path, string json)
    {
        var read = Decode(path, json);

        Assert.Equal(ContractRefusal.Unreadable, read.Refusal);
        Assert.Null(read.Message);
    }

    [Fact]
    public void NothingIsUnreadable()
    {
        Assert.Equal(ContractRefusal.Unreadable, DashCodec.Decode(WatchIdentity.DashPeoplePath, null).Refusal);
        Assert.Equal(ContractRefusal.Unreadable, DashCodec.Decode(WatchIdentity.DashPeoplePath, []).Refusal);
    }

    [Theory]
    [InlineData("people ask", "/dash/thread")]
    [InlineData("people", "/dash/mail")]
    [InlineData("reply", "/dash/call-note")]
    [InlineData("file a correspondence", "/dash/delete")]
    [InlineData("delete a message", "/dash/file")]
    [InlineData("mail", "/dash/people")]
    [InlineData("people ask", "/update/installed")]
    [InlineData("people ask", "/dash/elsewhere")]
    [InlineData("people ask", null)]
    public void AKindIsReadOnlyOnItsOwnPath(string name, string? path)
    {
        var read = DashCodec.Decode(path, DashCodec.Encode(Messages[name]));

        Assert.Equal(ContractRefusal.Unreadable, read.Refusal);
        Assert.Null(read.Message);
    }

    [Fact]
    public void AnAskIsWhatItsPathSays()
    {
        // An ask carries nothing but its kind, so the path it arrived on is what it asks for.
        var ask = DashCodec.Encode(new DashPeopleAsk());

        Assert.IsType<DashPeopleAsk>(DashCodec.Decode(WatchIdentity.DashPeoplePath, ask).Message);
        Assert.IsType<DashMailAsk>(DashCodec.Decode(WatchIdentity.DashMailPath, ask).Message);
    }

    [Fact]
    public void TooLargeIsUnreadable()
    {
        var padding = new string(' ', DashLimits.MaxBytes);
        var read = Decode(WatchIdentity.DashPeoplePath, "{\"v\":1,\"kind\":\"ask\"}" + padding);

        Assert.Equal(ContractRefusal.Unreadable, read.Refusal);
    }

    [Fact]
    public void PastALimitIsUnreadable()
    {
        var tooMany = new DashPeople(Enumerable.Range(0, DashLimits.People + 1)
            .Select(index => new DashPerson(LeeKey, "Lee", "Hi", T0, false, false)).ToList());
        var tooLong = new DashPeople([new DashPerson(LeeKey, new string('e', DashLimits.Name + 1), "Hi", T0, false, false)]);
        var longLine = new DashPeople([new DashPerson(LeeKey, "Lee", new string('e', DashLimits.Line + 1), T0, false, false)]);
        var longText = new DashSend("send-1", LeeKey, [], new string('e', DashLimits.SendText + 1));

        Assert.Equal(ContractRefusal.Unreadable, DashCodec.Decode(WatchIdentity.DashPeoplePath, DashCodec.Encode(tooMany)).Refusal);
        Assert.Equal(ContractRefusal.Unreadable, DashCodec.Decode(WatchIdentity.DashPeoplePath, DashCodec.Encode(tooLong)).Refusal);
        Assert.Equal(ContractRefusal.Unreadable, DashCodec.Decode(WatchIdentity.DashPeoplePath, DashCodec.Encode(longLine)).Refusal);
        Assert.Equal(ContractRefusal.Unreadable, DashCodec.Decode(WatchIdentity.DashSendPath, DashCodec.Encode(longText)).Refusal);
    }

    [Fact]
    public void TheFullestMessagesFitTheBudget()
    {
        // Three bytes a character, every list at its limit: people, mail lines, a send and a note still fit. A
        // conversation is the one Post trims to fit (DashRelay), since its messages carry the most words.
        const char Wide = '漢';
        string Full(int length) => new(Wide, length);
        var people = new DashPeople(Enumerable.Range(0, DashLimits.People)
            .Select(index => new DashPerson(LeeKey, Full(DashLimits.Name), Full(DashLimits.Line), T0, true, true)).ToList());
        var mail = new DashMail(Enumerable.Range(0, DashLimits.MailLines)
            .Select(index => new DashMailLine(TextId, Full(DashLimits.Name), Full(DashLimits.Subject), T0)).ToList());
        var send = new DashSend("send-1", null, Enumerable.Range(0, DashLimits.Recipients).Select(index => new string('5', DashLimits.Number)).ToList(),
            Full(DashLimits.SendText));
        var note = new DashCallNote(CallId, null, Full(DashLimits.SendText));
        var file = new DashFile("act-1", null, Enumerable.Range(0, DashLimits.Messages).Select(index => TextId).ToList(), DashLimits.MaxWindow);

        Assert.All(new DashMessage[] { people, mail, send, note, file }, message =>
        {
            var bytes = DashCodec.Encode(message);
            Assert.True(bytes.Length <= DashLimits.MaxBytes, $"{message.GetType().Name}: {bytes.Length} bytes");
            Assert.True(DashCodec.Decode(message.Path, bytes).Accepted, message.GetType().Name);
        });
    }

    [Fact]
    public void TheLimitsAreTheDecidedOnes()
    {
        Assert.Equal(24, DashLimits.People);
        Assert.Equal(16, DashLimits.Messages);
        Assert.Equal(16, DashLimits.MailLines);
        Assert.Equal(12, DashLimits.Members);
        Assert.Equal(20, DashLimits.Recipients);
        Assert.Equal(8, DashLimits.Attachments);
        Assert.Equal(60, DashLimits.Name);
        Assert.Equal(120, DashLimits.Line);
        Assert.Equal(640, DashLimits.MessageText);
        Assert.Equal(120, DashLimits.Subject);
        Assert.Equal(4000, DashLimits.SendText);
        Assert.Equal(40, DashLimits.Number);
        Assert.Equal(TimeSpan.FromMinutes(2), DashLimits.MaxWindow);
        Assert.Equal(64 * 1024, DashLimits.MaxBytes);
    }

    [Fact]
    public void TheWordsAreFrozen()
    {
        string[] envelope =
        [
            DashCodec.VersionField, DashCodec.KindField, DashCodec.AskKind, DashCodec.PeopleKind, DashCodec.ThreadKind, DashCodec.SendKind,
            DashCodec.ResultKind, DashCodec.FileKind, DashCodec.DeleteKind, DashCodec.UndoKind, DashCodec.ReadKind, DashCodec.NoteKind,
            DashCodec.MailKind,
        ];
        string[] fields =
        [
            DashCodec.PeopleField, DashCodec.KeyField, DashCodec.NameField, DashCodec.LineField, DashCodec.AtField, DashCodec.UnreadField,
            DashCodec.GroupField, DashCodec.TitleField, DashCodec.MembersField, DashCodec.MessagesField, DashCodec.IdField,
            DashCodec.IncomingField, DashCodec.TypeField, DashCodec.TextField, DashCodec.FromField, DashCodec.AttachmentsField,
            DashCodec.ToField, DashCodec.OutcomeField, DashCodec.IdsField, DashCodec.WindowField, DashCodec.ActField,
            DashCodec.ThroughField, DashCodec.CallField, DashCodec.NumberField, DashCodec.MailField, DashCodec.SubjectField,
        ];

        Assert.Equal(["v", "kind", "ask", "people", "thread", "send", "result", "file", "delete", "undo", "read", "note", "mail"], envelope);
        Assert.Equal(
            ["people", "key", "name", "line", "at", "unread", "group", "title", "members", "messages", "id", "incoming", "type", "text",
                "from", "attachments", "to", "outcome", "ids", "window", "act", "through", "call", "number", "mail", "subject"],
            fields);
    }

    [Fact]
    public void TheTypeWordsAreFrozen()
    {
        Assert.Equal(["text", "call", "voicemail", "note"], Enum.GetValues<DashMessageType>().Select(DashCodec.Word));
        Assert.Equal(["photo", "video", "file"], Enum.GetValues<DashAttachmentType>().Select(DashCodec.Word));
        Assert.Equal(["sent", "no-route", "nowhere", "failed"], Enum.GetValues<DashSendOutcome>().Select(DashCodec.Word));
    }
}
