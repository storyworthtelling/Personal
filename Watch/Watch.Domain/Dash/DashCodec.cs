using System.Text.Encodings.Web;
using System.Text.Json;
using Watch.Domain.Updates;

namespace Watch.Domain.Dash;

/// <summary>
/// Dash's message codec (plan §2.5): UTF-8 JSON with an integer <c>v</c> and a <c>kind</c>, one shape per contract. A
/// photo's JPEG crosses as base64 in its own message (<c>dash/photo</c>, B5), refused unless it is a JPEG within its budget.
/// A message is read for the path it arrived on, so an ask on <c>dash/thread</c> is never taken for one on
/// <c>dash/people</c>. A newer <c>v</c> is refused before anything else is read; a missing, mistyped, over-long or
/// out-of-place field refuses the whole message.
/// </summary>
public static class DashCodec
{
    /// <summary>The version both ends write and read.</summary>
    public const int Version = 1;

    // Wire words, frozen: the envelope and the kinds.
    public const string VersionField = "v";
    public const string KindField = "kind";
    public const string AskKind = "ask";
    public const string PeopleKind = "people";
    public const string ThreadKind = "thread";
    public const string SendKind = "send";
    public const string ResultKind = "result";
    public const string FileKind = "file";
    public const string DeleteKind = "delete";
    public const string UndoKind = "undo";
    public const string ReadKind = "read";
    public const string NoteKind = "note";
    public const string MailKind = "mail";
    public const string PhotoKind = "photo";

    // Wire words, frozen: the fields.
    public const string PeopleField = "people";
    public const string KeyField = "key";
    public const string NameField = "name";
    public const string LineField = "line";
    public const string AtField = "at";
    public const string UnreadField = "unread";
    public const string GroupField = "group";
    public const string TitleField = "title";
    public const string MembersField = "members";
    public const string MessagesField = "messages";
    public const string IdField = "id";
    public const string IncomingField = "incoming";
    public const string TypeField = "type";
    public const string TextField = "text";
    public const string FromField = "from";
    public const string AttachmentsField = "attachments";
    public const string ToField = "to";
    public const string OutcomeField = "outcome";
    public const string IdsField = "ids";
    public const string WindowField = "window";
    public const string ActField = "act";
    public const string ThroughField = "through";
    public const string CallField = "call";
    public const string NumberField = "number";
    public const string MailField = "mail";
    public const string SubjectField = "subject";
    public const string JpegField = "jpeg";

    // Wire words, frozen, in each enum's order.
    private static readonly string[] MessageTypeWords = ["text", "call", "voicemail", "note"];
    private static readonly string[] AttachmentTypeWords = ["photo", "video", "file"];
    private static readonly string[] SendOutcomeWords = ["sent", "no-route", "nowhere", "failed"];

    // The latest instant a DateTimeOffset holds, in Unix milliseconds.
    private const long LastInstant = 253_402_300_799_999;

    // Every character a phone can type is written as itself, not as \uXXXX, so a message keeps to its budget. This is
    // JSON between two of the suite's own apps, never HTML, which is the only place the relaxed escaping is unsafe.
    private static readonly JsonWriterOptions Writing = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static byte[] Encode(DashMessage message)
    {
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json, Writing))
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionField, Version);
            switch (message)
            {
                case DashPeopleAsk or DashMailAsk:
                    writer.WriteString(KindField, AskKind);
                    break;
                case DashThreadAsk ask:
                    writer.WriteString(KindField, AskKind);
                    writer.WriteString(KeyField, ask.Key);
                    break;
                case DashPeople people:
                    writer.WriteString(KindField, PeopleKind);
                    WritePeople(writer, people);
                    break;
                case DashThread thread:
                    writer.WriteString(KindField, ThreadKind);
                    WriteThread(writer, thread);
                    break;
                case DashSend send:
                    writer.WriteString(KindField, SendKind);
                    writer.WriteString(IdField, send.Id);
                    WriteTarget(writer, send.Key, ToField, send.To);
                    writer.WriteString(TextField, send.Text);
                    break;
                case DashSent sent:
                    writer.WriteString(KindField, ResultKind);
                    writer.WriteString(IdField, sent.Id);
                    writer.WriteString(OutcomeField, Word(sent.Outcome));
                    break;
                case DashFile file:
                    WriteAct(writer, FileKind, file.Id, file.Key, file.Messages, file.Window);
                    break;
                case DashDelete delete:
                    WriteAct(writer, DeleteKind, delete.Id, delete.Key, delete.Messages, delete.Window);
                    break;
                case DashUndo undo:
                    writer.WriteString(KindField, UndoKind);
                    writer.WriteString(ActField, undo.Act);
                    break;
                case DashRead read:
                    writer.WriteString(KindField, ReadKind);
                    writer.WriteString(KeyField, read.Key);
                    writer.WriteNumber(ThroughField, read.Through.ToUnixTimeMilliseconds());
                    break;
                case DashCallNote note:
                    writer.WriteString(KindField, NoteKind);
                    WriteOptional(writer, CallField, note.Call);
                    WriteOptional(writer, NumberField, note.Number);
                    writer.WriteString(TextField, note.Text);
                    break;
                case DashMail mail:
                    writer.WriteString(KindField, MailKind);
                    WriteMail(writer, mail);
                    break;
                case DashPhotoAsk ask:
                    writer.WriteString(KindField, AskKind);
                    writer.WriteString(IdField, ask.Id);
                    break;
                case DashPhoto photo:
                    writer.WriteString(KindField, PhotoKind);
                    writer.WriteString(IdField, photo.Id);
                    writer.WriteBase64String(JpegField, photo.Jpeg);
                    break;
                default:
                    throw new ArgumentException($"{message.GetType().Name} is not a Dash message.", nameof(message));
            }
            writer.WriteEndObject();
        }
        return json.ToArray();
    }

    /// <summary>The message that arrived on <paramref name="path"/>, or why it was refused.</summary>
    public static ContractRead<DashMessage> Decode(string? path, byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length > DashLimits.MaxBytes)
        {
            return ContractRead<DashMessage>.Refused(ContractRefusal.Unreadable, 0);
        }
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ContractRead<DashMessage>.Refused(ContractRefusal.Unreadable, 0);
            }

            var version = ContractVersion.Of(root, VersionField);
            if (version <= 0)
            {
                return ContractRead<DashMessage>.Refused(ContractRefusal.Unreadable, 0);
            }
            if (version > Version)
            {
                return ContractRead<DashMessage>.Refused(ContractRefusal.Newer, version);
            }

            var kind = root.TryGetProperty(KindField, out var said) && said.ValueKind == JsonValueKind.String ? said.GetString() : null;
            var message = Read(path, kind, root);
            return message is null
                ? ContractRead<DashMessage>.Refused(ContractRefusal.Unreadable, version)
                : ContractRead<DashMessage>.Read(message, version);
        }
        catch (JsonException)
        {
            // Not JSON: nothing in it can be trusted, so it is refused as unreadable, and the line says so.
            return ContractRead<DashMessage>.Refused(ContractRefusal.Unreadable, 0);
        }
    }

    /// <summary>A message type's wire word.</summary>
    public static string Word(DashMessageType type) => MessageTypeWords[(int)type];

    /// <summary>An attachment type's wire word.</summary>
    public static string Word(DashAttachmentType type) => AttachmentTypeWords[(int)type];

    /// <summary>A send outcome's wire word.</summary>
    public static string Word(DashSendOutcome outcome) => SendOutcomeWords[(int)outcome];

    // ---- writing ----

    private static void WritePeople(Utf8JsonWriter writer, DashPeople people)
    {
        writer.WritePropertyName(PeopleField);
        writer.WriteStartArray();
        foreach (var person in people.People)
        {
            writer.WriteStartObject();
            writer.WriteString(KeyField, person.Key);
            writer.WriteString(NameField, person.Name);
            writer.WriteString(LineField, person.Line);
            writer.WriteNumber(AtField, person.At.ToUnixTimeMilliseconds());
            writer.WriteBoolean(UnreadField, person.Unread);
            writer.WriteBoolean(GroupField, person.Group);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteThread(Utf8JsonWriter writer, DashThread thread)
    {
        writer.WriteString(KeyField, thread.Key);
        writer.WriteString(TitleField, thread.Title);
        writer.WriteBoolean(GroupField, thread.Group);
        WriteStrings(writer, MembersField, thread.Members);
        writer.WritePropertyName(MessagesField);
        writer.WriteStartArray();
        foreach (var message in thread.Messages)
        {
            writer.WriteStartObject();
            writer.WriteString(IdField, message.Id);
            writer.WriteNumber(AtField, message.At.ToUnixTimeMilliseconds());
            writer.WriteBoolean(IncomingField, message.Incoming);
            writer.WriteString(TypeField, Word(message.Type));
            writer.WriteString(TextField, message.Text);
            WriteOptional(writer, FromField, message.From);
            writer.WritePropertyName(AttachmentsField);
            writer.WriteStartArray();
            foreach (var attachment in message.Attachments)
            {
                writer.WriteStartObject();
                writer.WriteString(IdField, attachment.Id);
                writer.WriteString(TypeField, Word(attachment.Type));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteMail(Utf8JsonWriter writer, DashMail mail)
    {
        writer.WritePropertyName(MailField);
        writer.WriteStartArray();
        foreach (var line in mail.Mail)
        {
            writer.WriteStartObject();
            writer.WriteString(IdField, line.Id);
            writer.WriteString(FromField, line.From);
            writer.WriteString(SubjectField, line.Subject);
            writer.WriteNumber(AtField, line.At.ToUnixTimeMilliseconds());
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteAct(Utf8JsonWriter writer, string kind, string id, string? key, IReadOnlyList<string> messages, TimeSpan window)
    {
        writer.WriteString(KindField, kind);
        writer.WriteString(IdField, id);
        WriteTarget(writer, key, IdsField, messages);
        writer.WriteNumber(WindowField, (long)window.TotalMilliseconds);
    }

    // A correspondence or a list, whichever the message names; the other is left out.
    private static void WriteTarget(Utf8JsonWriter writer, string? key, string listField, IReadOnlyList<string> list)
    {
        WriteOptional(writer, KeyField, key);
        if (list.Count > 0)
        {
            WriteStrings(writer, listField, list);
        }
    }

    private static void WriteStrings(Utf8JsonWriter writer, string field, IReadOnlyList<string> values)
    {
        writer.WritePropertyName(field);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string field, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(field, value);
        }
    }

    // ---- reading ----

    private static DashMessage? Read(string? path, string? kind, JsonElement root) => (path, kind) switch
    {
        (WatchIdentity.DashPeoplePath, AskKind) => new DashPeopleAsk(),
        (WatchIdentity.DashPeoplePath, PeopleKind) => ReadPeople(root),
        (WatchIdentity.DashThreadPath, AskKind) => Id(root, KeyField, DashIds.IsCorrespondence, out var key) ? new DashThreadAsk(key) : null,
        (WatchIdentity.DashThreadPath, ThreadKind) => ReadThread(root),
        (WatchIdentity.DashSendPath, SendKind) => ReadSend(root),
        (WatchIdentity.DashSendPath, ResultKind) => ReadResult(root),
        (WatchIdentity.DashFilePath, FileKind) => ReadAct(root) is { } act ? new DashFile(act.Id, act.Key, act.Messages, act.Window) : null,
        (WatchIdentity.DashDeletePath, DeleteKind) => ReadAct(root) is { } act ? new DashDelete(act.Id, act.Key, act.Messages, act.Window) : null,
        (WatchIdentity.DashUndoPath, UndoKind) => Id(root, ActField, DashIds.IsAct, out var undone) ? new DashUndo(undone) : null,
        (WatchIdentity.DashReadPath, ReadKind) => ReadRead(root),
        (WatchIdentity.DashCallNotePath, NoteKind) => ReadNote(root),
        (WatchIdentity.DashMailPath, AskKind) => new DashMailAsk(),
        (WatchIdentity.DashMailPath, MailKind) => ReadMail(root),
        (WatchIdentity.DashPhotoPath, AskKind) => Id(root, IdField, DashIds.IsAttachment, out var photo) ? new DashPhotoAsk(photo) : null,
        (WatchIdentity.DashPhotoPath, PhotoKind) => ReadPhoto(root),
        _ => null,
    };

    private static DashPeople? ReadPeople(JsonElement root)
    {
        if (!Items(root, PeopleField, DashLimits.People, out var entries))
        {
            return null;
        }

        var people = new List<DashPerson>();
        foreach (var entry in entries)
        {
            if (!Id(entry, KeyField, DashIds.IsCorrespondence, out var key)
                || !Text(entry, NameField, DashLimits.Name, out var name)
                || !Text(entry, LineField, DashLimits.Line, out var line, mayBeEmpty: true)
                || !Instant(entry, AtField, out var at)
                || !Flag(entry, UnreadField, out var unread)
                || !Flag(entry, GroupField, out var group))
            {
                return null;
            }
            people.Add(new DashPerson(key, name, line, at, unread, group));
        }
        return new DashPeople(people);
    }

    private static DashThread? ReadThread(JsonElement root)
    {
        if (!Id(root, KeyField, DashIds.IsCorrespondence, out var key)
            || !Text(root, TitleField, DashLimits.Name, out var title)
            || !Flag(root, GroupField, out var group)
            || !Strings(root, MembersField, DashLimits.Members, DashLimits.Name, out var members)
            || !Items(root, MessagesField, DashLimits.Messages, out var entries))
        {
            return null;
        }

        var messages = new List<DashThreadMessage>();
        foreach (var entry in entries)
        {
            if (!Id(entry, IdField, DashIds.IsMessage, out var id)
                || !Instant(entry, AtField, out var at)
                || !Flag(entry, IncomingField, out var incoming)
                || !Word(entry, TypeField, MessageTypeWords, out var type)
                || !Text(entry, TextField, DashLimits.MessageText, out var text, mayBeEmpty: true)
                || !OptionalText(entry, FromField, DashLimits.Name, out var from)
                || !Items(entry, AttachmentsField, DashLimits.Attachments, out var attached))
            {
                return null;
            }

            var attachments = new List<DashAttachment>();
            foreach (var attachment in attached)
            {
                if (!Id(attachment, IdField, DashIds.IsAttachment, out var attachmentId)
                    || !Word(attachment, TypeField, AttachmentTypeWords, out var attachmentType))
                {
                    return null;
                }
                attachments.Add(new DashAttachment(attachmentId, (DashAttachmentType)attachmentType));
            }
            messages.Add(new DashThreadMessage(id, at, incoming, (DashMessageType)type, text, from, attachments));
        }
        return new DashThread(key, title, group, members, messages);
    }

    private static DashSend? ReadSend(JsonElement root)
    {
        if (!Id(root, IdField, DashIds.IsAct, out var id)
            || !OptionalId(root, KeyField, DashIds.IsCorrespondence, out var key)
            || !OptionalStrings(root, ToField, DashLimits.Recipients, DashLimits.Number, out var to)
            || !Words(root, TextField, DashLimits.SendText, out var text)
            || (key is null) == (to.Count == 0))
        {
            return null;
        }
        return new DashSend(id, key, to, text);
    }

    private static DashSent? ReadResult(JsonElement root) =>
        Id(root, IdField, DashIds.IsAct, out var id) && Word(root, OutcomeField, SendOutcomeWords, out var outcome)
            ? new DashSent(id, (DashSendOutcome)outcome)
            : null;

    private static (string Id, string? Key, IReadOnlyList<string> Messages, TimeSpan Window)? ReadAct(JsonElement root)
    {
        if (!Id(root, IdField, DashIds.IsAct, out var id)
            || !OptionalId(root, KeyField, DashIds.IsCorrespondence, out var key)
            || !OptionalIds(root, IdsField, DashLimits.Messages, DashIds.IsMessage, out var messages)
            || (key is null) == (messages.Count == 0)
            || !root.TryGetProperty(WindowField, out var said) || said.ValueKind != JsonValueKind.Number
            || !said.TryGetInt64(out var milliseconds)
            || milliseconds <= 0 || milliseconds > (long)DashLimits.MaxWindow.TotalMilliseconds)
        {
            return null;
        }
        return (id, key, messages, TimeSpan.FromMilliseconds(milliseconds));
    }

    private static DashRead? ReadRead(JsonElement root) =>
        Id(root, KeyField, DashIds.IsCorrespondence, out var key) && Instant(root, ThroughField, out var through)
            ? new DashRead(key, through)
            : null;

    private static DashCallNote? ReadNote(JsonElement root)
    {
        if (!OptionalId(root, CallField, DashIds.IsMessage, out var call)
            || !OptionalText(root, NumberField, DashLimits.Number, out var number)
            || !Words(root, TextField, DashLimits.SendText, out var text)
            || (call is null) == (number is null))
        {
            return null;
        }
        return new DashCallNote(call, number, text);
    }

    private static DashMail? ReadMail(JsonElement root)
    {
        if (!Items(root, MailField, DashLimits.MailLines, out var entries))
        {
            return null;
        }

        var lines = new List<DashMailLine>();
        foreach (var entry in entries)
        {
            if (!Id(entry, IdField, DashIds.IsMessage, out var id)
                || !Text(entry, FromField, DashLimits.Name, out var from)
                || !Text(entry, SubjectField, DashLimits.Subject, out var subject, mayBeEmpty: true)
                || !Instant(entry, AtField, out var at))
            {
                return null;
            }
            lines.Add(new DashMailLine(id, from, subject, at));
        }
        return new DashMail(lines);
    }

    // A photo's id and its JPEG, as base64: no more than the budget, and a JPEG by its own first bytes.
    private static DashPhoto? ReadPhoto(JsonElement root)
    {
        if (!Id(root, IdField, DashIds.IsAttachment, out var id)
            || !root.TryGetProperty(JpegField, out var said) || said.ValueKind != JsonValueKind.String
            || !said.TryGetBytesFromBase64(out var jpeg)
            || jpeg is null || jpeg.Length > DashLimits.PhotoBytes || !IsJpeg(jpeg))
        {
            return null;
        }
        return new DashPhoto(id, jpeg);
    }

    // A JPEG opens with its start-of-image marker and the first byte of the marker after it.
    private static bool IsJpeg(byte[] bytes) => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    // ---- fields ----

    private static bool Text(JsonElement element, string field, int longest, out string value, bool mayBeEmpty = false)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(field, out var said) || said.ValueKind != JsonValueKind.String
            || said.GetString() is not { } text
            || text.Length > longest || (text.Length == 0 && !mayBeEmpty))
        {
            return false;
        }
        value = text;
        return true;
    }

    // Absent (or null) is no value; present, it must be a string of one to the longest characters.
    private static bool OptionalText(JsonElement element, string field, int longest, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(field, out var said) || said.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (!Text(element, field, longest, out var text))
        {
            return false;
        }
        value = text;
        return true;
    }

    // Words someone wrote: at least one that is not a space.
    private static bool Words(JsonElement element, string field, int longest, out string value) =>
        Text(element, field, longest, out value) && !string.IsNullOrWhiteSpace(value);

    private static bool Id(JsonElement element, string field, Func<string?, bool> shape, out string value) =>
        Text(element, field, DashIds.MaxActLength, out value) && shape(value);

    private static bool OptionalId(JsonElement element, string field, Func<string?, bool> shape, out string? value) =>
        OptionalText(element, field, DashIds.MaxActLength, out value) && (value is null || shape(value));

    private static bool Flag(JsonElement element, string field, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(field, out var said) || said.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }
        value = said.GetBoolean();
        return true;
    }

    // Whole Unix milliseconds, no earlier than 1970 and within what a DateTimeOffset holds.
    private static bool Instant(JsonElement element, string field, out DateTimeOffset value)
    {
        value = default;
        if (!element.TryGetProperty(field, out var said) || said.ValueKind != JsonValueKind.Number
            || !said.TryGetInt64(out var milliseconds) || milliseconds < 0 || milliseconds > LastInstant)
        {
            return false;
        }
        value = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        return true;
    }

    private static bool Word(JsonElement element, string field, string[] words, out int index)
    {
        index = -1;
        if (!Text(element, field, DashIds.MaxActLength, out var word))
        {
            return false;
        }
        index = Array.IndexOf(words, word);
        return index >= 0;
    }

    private static bool Items(JsonElement element, string field, int most, out List<JsonElement> items)
    {
        items = [];
        if (!element.TryGetProperty(field, out var said) || said.ValueKind != JsonValueKind.Array || said.GetArrayLength() > most)
        {
            return false;
        }
        items = [.. said.EnumerateArray()];
        return true;
    }

    private static bool Strings(JsonElement element, string field, int most, int longest, out IReadOnlyList<string> values)
    {
        values = [];
        if (!Items(element, field, most, out var items))
        {
            return false;
        }

        var read = new List<string>();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } value || value.Length > longest)
            {
                return false;
            }
            read.Add(value);
        }
        values = read;
        return true;
    }

    private static bool OptionalStrings(JsonElement element, string field, int most, int longest, out IReadOnlyList<string> values)
    {
        values = [];
        return !element.TryGetProperty(field, out _) || Strings(element, field, most, longest, out values);
    }

    private static bool OptionalIds(JsonElement element, string field, int most, Func<string?, bool> shape, out IReadOnlyList<string> values) =>
        OptionalStrings(element, field, most, DashIds.MaxActLength, out values) && values.All(value => shape(value));
}
