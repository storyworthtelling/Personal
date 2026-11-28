using Watch.Domain.Dash;

namespace Watch.Tests.Dash;

/// <summary>
/// One message of each shape Dash's contracts carry, with invented people (Sam, Lee, Robin, Juniper) and literal ids
/// in the shape <see cref="DashIds"/> writes, so the fixtures stand on their own.
/// </summary>
internal static class DashFixtures
{
    public const string LeeKey = "c0123456789abcdef";
    public const string GroupKey = "cfedcba9876543210";
    public const string TextId = "m00112233445566ff";
    public const string CallId = "m0a1b2c3d4e5f6071";
    public const string PhotoId = "a8899aabbccddeeff";

    // B6: the Rolodex (W-5.1.2): a contact is a name and an id, never a number; a page names its edition.
    public const string LeeContact = "p0123456789abcdef";
    public const string RobinContact = "p1122334455667788";
    public const string Edition = "e0123456789abcdef";

    public static readonly DateTimeOffset T0 = new(2026, 11, 21, 16, 20, 0, 0, TimeSpan.Zero);

    /// <summary>The start of a JPEG (its SOI marker, then an APP0 marker) and a few bytes: not a picture, but a JPEG's shape.</summary>
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9];

    public static readonly DashPeople People = new(
    [
        new DashPerson(LeeKey, "Lee", "Running late. I’ll be at the field by four.", T0, Unread: true, Group: false),
        new DashPerson(GroupKey, "Team snacks", "Photo", T0.AddHours(-1), Unread: false, Group: true),
    ]);

    public static readonly DashThread GroupThread = new(
        GroupKey,
        "Team snacks",
        Group: true,
        ["Lee", "Robin", "Sam"],
        [
            new DashThreadMessage(TextId, T0.AddMinutes(-70), Incoming: false, DashMessageType.Text, "Are you bringing the oranges?", null, []),
            new DashThreadMessage("m1111111111111111", T0.AddMinutes(-60), Incoming: true, DashMessageType.Text, "Photo", "Robin",
                [new DashAttachment(PhotoId, DashAttachmentType.Photo), new DashAttachment("a2222222222222222", DashAttachmentType.Video)]),
            new DashThreadMessage(CallId, T0.AddMinutes(-30), Incoming: true, DashMessageType.Call, "Missed call", "Lee", []),
            new DashThreadMessage("m3333333333333333", T0.AddMinutes(-20), Incoming: true, DashMessageType.Voicemail, "Voicemail", "Sam",
                [new DashAttachment("a4444444444444444", DashAttachmentType.File)]),
            new DashThreadMessage("m5555555555555555", T0.AddMinutes(-10), Incoming: false, DashMessageType.Note, "Pickup moves to 3:15 on Friday.", null, []),
        ]);

    public static readonly Dictionary<string, DashMessage> Messages = new(StringComparer.Ordinal)
    {
        ["people ask"] = new DashPeopleAsk(),
        ["people"] = People,
        ["nobody waiting"] = new DashPeople([]),
        ["thread ask"] = new DashThreadAsk(LeeKey),
        ["thread"] = GroupThread,
        ["person's thread"] = new DashThread(LeeKey, "Lee", Group: false, [],
            [new DashThreadMessage(TextId, T0, Incoming: true, DashMessageType.Text, "Running late.", null, [])]),
        ["reply"] = new DashSend("send-1", LeeKey, [], "No rush, I’ll save you a spot by the bench."),
        ["new text"] = new DashSend("send-2", null, ["+1 212-555-0101", "+12125550102"], "On my way."),
        ["sent"] = new DashSent("send-1", DashSendOutcome.Sent),
        ["no route"] = new DashSent("send-2", DashSendOutcome.NoRoute),
        ["file a correspondence"] = new DashFile("act-1", LeeKey, [], TimeSpan.FromSeconds(6)),
        ["file messages"] = new DashFile("act-2", null, [TextId, CallId], TimeSpan.FromSeconds(30)),
        ["delete a correspondence"] = new DashDelete("act-3", GroupKey, [], TimeSpan.FromSeconds(6)),
        ["delete a message"] = new DashDelete("act-4", null, [TextId], TimeSpan.FromMinutes(2)),
        ["undo"] = new DashUndo("act-3"),
        ["read"] = new DashRead(LeeKey, T0),
        ["note on a call"] = new DashCallNote(CallId, null, "Robin can take Juniper on Thursday.") { At = T0.AddMinutes(2) },
        ["note on a number"] = new DashCallNote(null, "+1 212-555-0103", "Call back about the dentist."),
        ["photo ask"] = new DashPhotoAsk(PhotoId),
        ["photo"] = new DashPhoto(PhotoId, Jpeg),
        ["rolodex ask"] = new DashRolodexAsk(0),
        ["rolodex"] = new DashRolodex(0, 2, Edition, [new DashContact(LeeContact, "Lee"), new DashContact(RobinContact, "Robin")]),
        ["empty rolodex"] = new DashRolodex(0, 1, Edition, []),
        ["new text to a contact"] = new DashSend("send-3", null, [], "Running five minutes late.") { Contact = LeeContact },
        // B7: the call that just ended (W-5.1.5): the wrist asks by the moment it ended, Post names it or says it has none.
        ["call ask"] = new DashCallAsk(T0),
        ["call"] = new DashCall(T0, CallId, "Robin", TimeSpan.FromMinutes(6)),
        ["call with no name"] = new DashCall(T0, CallId, null, TimeSpan.Zero),
        ["no call"] = new DashCall(T0, null, null, null),
        ["mail ask"] = new DashMailAsk(),
        ["mail"] = new DashMail(
        [
            new DashMailLine("m6666666666666666", "Sam", "Saturday plans", T0.AddMinutes(-20)),
            new DashMailLine("m7777777777777777", "Robin", "", T0.AddHours(-2)),
        ]),
    };

    public static TheoryData<string> Names() => [.. Messages.Keys];
}
