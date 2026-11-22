using System.Text.RegularExpressions;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Storyworthtelling.Folds;
using Watch.Domain.Dash;

namespace Codex.Infrastructure.Dash;

/// <summary>
/// What Post holds when the wrist asks or acts: the timeline as the Mailbox reads it and the stores that decide what it
/// shows. The head reads them; the relay only decides. <paramref name="NameOf"/> is the address book's name for a key.
/// </summary>
public sealed record DashSnapshot(
    IReadOnlyList<TimelineItem> Items,
    IReadOnlySet<(TimelineItemKind Kind, string SourceId)> Hidden,
    SpamList Spam,
    DeleteBuffer Deletes,
    Tombstones Tombstones,
    IIdentityResolver Resolver,
    ReadState ReadState,
    GroupNames GroupNames,
    Func<string, string> NameOf);

/// <summary>
/// How a send from the wrist goes, the way the phone's would: the bridge first when it is connected, then
/// <paramref name="Otherwise"/> (a plain text to a person, one MMS to a group) if the bridge is down or refuses it.
/// <paramref name="Refused"/> says why it cannot go at all.
/// </summary>
public sealed record DashSendPlan(
    IReadOnlyList<string> Recipients, string? ConversationId, bool TryBridge, TextSendPath Otherwise, DashSendOutcome? Refused);

/// <summary>
/// A file or a delete from the wrist: the messages it takes, the window it is held for, and when the sync that carries
/// it to the source may run (<see cref="SyncMoment"/>, after the window, so an undo inside it is never a lie).
/// </summary>
public sealed record DashActPlan(DashOutcome Outcome, IReadOnlyList<TimelineItem> Items, TimeSpan Window, TimeSpan SyncAfter);

/// <summary>A read from the wrist: the thread to stamp read, and the stamp.</summary>
public sealed record DashReadPlan(DashOutcome Outcome, string? ThreadKey, DateTimeOffset Stamp);

/// <summary>A call note from the wrist, as the timeline item Post keeps it as (<see cref="CallNote"/>).</summary>
public sealed record DashNotePlan(DashOutcome Outcome, TimelineItem? Note);

/// <summary>
/// Post's half of Dash's contracts (P14 W-4.1.1): it answers <c>dash/people</c>, <c>dash/thread</c> and <c>dash/mail</c>
/// from the timeline as the Mailbox shows it, and turns each act from the wrist into Post's own rules: a send takes the
/// phone's own route, a file keeps the tombstone rule, a delete is held for the undo window, an undo takes back what its
/// act took, a read on the watch is a read on the phone (A14), and a call note is kept where Post keeps call notes.
/// Post stays the one place the texts live (A9): the watch is shown, never handed, the record. Pure: the head reads the
/// stores, carries the plans out, and logs through <see cref="DashLogLines"/>, never a payload.
/// </summary>
public sealed partial class DashRelay
{
    /// <summary>
    /// How much longer than the wrist's window Post holds an act, so an undo tapped as the ring empties still lands over
    /// Bluetooth or the cloud relay. Chosen, not measured: the device run says.
    /// </summary>
    public static readonly TimeSpan TransportGrace = TimeSpan.FromSeconds(10);

    private readonly DashSnapshot snapshot;
    private readonly IReadOnlyList<ContactThread> waiting;

    public DashRelay(DashSnapshot snapshot)
    {
        this.snapshot = snapshot;

        // The Mailbox's own view (MailboxView), less anything a standing tombstone says the owner filed: filing hides a
        // text and takes a mail out of the cache, and the tombstone is what keeps either from walking back in before the
        // source has let it go.
        var unfiled = snapshot.Items.Where(item => !snapshot.Tombstones.IsFiled(item.SourceId));
        waiting = MailboxView.Build(unfiled, snapshot.Hidden, snapshot.Spam, snapshot.Deletes, snapshot.Resolver, snapshot.ReadState).Waiting;
    }

    // ---- what the wrist is shown ----

    /// <summary>
    /// People waiting (WD §11): every correspondence the Mailbox shows that has a text, a call or a voicemail waiting, as
    /// one row each (a group too), newest first. Mail reaches the wrist as mail lines only (A11).
    /// </summary>
    public DashPeople People() =>
        new(waiting
            .Select(Person)
            .OfType<DashPerson>()
            .OrderByDescending(person => person.At)
            .ThenBy(person => person.Key, StringComparer.Ordinal)
            .Take(DashLimits.People)
            .ToList());

    /// <summary>A correspondence's last messages, oldest first, trimmed from the oldest until the message fits; null when
    /// the key names no correspondence with a text, call or voicemail waiting.</summary>
    public DashThread? Thread(string key)
    {
        if (Find(key) is not { } thread || Shown(thread) is not { Count: > 0 } items)
        {
            return null;
        }

        var members = thread.IsGroup
            ? thread.Participants.Take(DashLimits.Members).Select(member => Cut(snapshot.NameOf(member), DashLimits.Name)).ToList()
            : [];
        var shown = new DashThread(key, Title(thread), thread.IsGroup, members,
            items.TakeLast(DashLimits.Messages).Select(item => Message(thread, item)).ToList());
        while (shown.Messages.Count > 1 && DashCodec.Encode(shown).Length > DashLimits.MaxBytes)
        {
            shown = shown with { Messages = shown.Messages.Skip(1).ToList() };
        }
        return shown;
    }

    /// <summary>Mail lines (A11): incoming mail from people only, by Post's own sorting, newest first.</summary>
    public DashMail Mail() =>
        new(waiting
            .SelectMany(thread => thread.Items)
            .Where(item => item.Kind == TimelineItemKind.Email && item.Incoming && item.MailClass == MailClass.Human)
            .OrderByDescending(item => item.TimestampUtc)
            .ThenBy(item => item.SourceId, StringComparer.Ordinal)
            .Take(DashLimits.MailLines)
            .Select(item => new DashMailLine(
                DashIds.Message(item.SourceId),
                Cut(snapshot.NameOf(item.ContactKey), DashLimits.Name),
                Cut(OneLine(item.Subject ?? string.Empty), DashLimits.Subject),
                item.TimestampUtc))
            .ToList());

    // ---- what the wrist asks ----

    /// <summary>
    /// A send from the wrist takes the phone's own road (<see cref="TextSend"/>): Google Messages when the bridge is up, into
    /// the conversation the correspondence last spoke in; otherwise, or if the bridge refuses it, a plain text to a person
    /// and one MMS to a group. A reply goes to whoever the phone's own reply would (<see cref="ReplyTarget"/>).
    /// </summary>
    public DashSendPlan Send(DashSend send, bool bridgeConnected, bool canSendSms)
    {
        IReadOnlyList<string> recipients;
        string? conversation = null;
        if (send.Key is { } key)
        {
            if (Find(key) is not { } thread || ReplyTarget.For(thread) is not { } target)
            {
                return Refused(DashSendOutcome.Nowhere);
            }
            recipients = target.Recipients;
            conversation = target.ConversationId;
        }
        else
        {
            if (send.To.Count == 0 || !send.To.All(Dialable))
            {
                return Refused(DashSendOutcome.Nowhere);
            }
            recipients = send.To;
        }

        var tryBridge = TextSend.Choose(bridgeConnected, canSendSms) == TextSendPath.Bridge;
        var otherwise = TextSend.WithoutBridge(recipients.Count, canSendSms);
        return !tryBridge && otherwise == TextSendPath.Unavailable
            ? Refused(DashSendOutcome.NoRoute)
            : new DashSendPlan(recipients, conversation, tryBridge, otherwise, null);
    }

    /// <summary>
    /// Filing from the wrist is filing on the phone: each message is filed as the Mailbox files it (ghost kept, tombstone
    /// raised, hidden at once), and the delete it owes the source waits out the window (<see cref="SyncMoment"/>) so an
    /// undo inside it is never a lie.
    /// </summary>
    public DashActPlan File(DashFile act, DateTimeOffset now, TimeSpan phoneWindow, DashActs acts) =>
        Act(DashActKind.File, act.Id, act.Key, act.Messages, act.Window, now, phoneWindow, acts);

    /// <summary>
    /// Deleting from the wrist is undo-first, as on the phone: each message is held in the delete buffer for the window
    /// and deleted at the source when it closes.
    /// </summary>
    public DashActPlan Delete(DashDelete act, DateTimeOffset now, TimeSpan phoneWindow, DashActs acts) =>
        Act(DashActKind.Delete, act.Id, act.Key, act.Messages, act.Window, now, phoneWindow, acts);

    /// <summary>What an undo from the wrist takes back: a filing is unfiled, a delete is let go from the buffer.</summary>
    public static DashUndoPlan Undo(DashUndo undo, DateTimeOffset now, DashActs acts) => acts.Undo(undo.Act, now);

    /// <summary>
    /// Read on the watch is read on the phone (A14): the thread is stamped read through the newest message the wrist
    /// showed, never earlier than it already was, so a later arrival stays unread on both.
    /// </summary>
    public DashReadPlan Read(DashRead read)
    {
        if (Find(read.Key) is not { } thread)
        {
            return new DashReadPlan(DashOutcome.Unknown, null, default);
        }

        // The wire carries whole milliseconds, so the message the wrist showed is the newest within that millisecond.
        var through = read.Through.ToUnixTimeMilliseconds();
        var seen = Shown(thread).Where(item => item.TimestampUtc.ToUnixTimeMilliseconds() <= through).ToList();
        return seen.Count == 0
            ? new DashReadPlan(DashOutcome.Unknown, null, default)
            : new DashReadPlan(DashOutcome.Marked, thread.ContactKey, snapshot.ReadState.Through(thread.ContactKey, seen.Max(item => item.TimestampUtc)));
    }

    /// <summary>
    /// A note from the wrist is kept where Post keeps call notes (<see cref="Codex.Domain.Post.CallNote"/>): on the call's
    /// number, as the owner's own words, whether the call is one Post has or one the watch's own phone app had.
    /// </summary>
    public DashNotePlan CallNote(DashCallNote note, DateTimeOffset now)
    {
        string? number = null;
        if (note.Call is { } call)
        {
            number = snapshot.Items
                .FirstOrDefault(item => item.Kind == TimelineItemKind.Call && !IsNote(item) && DashIds.Message(item.SourceId) == call)?
                .ContactKey;
        }
        else if (note.Number is { } said && Dialable(said))
        {
            number = said;
        }

        return number is null
            ? new DashNotePlan(DashOutcome.Unknown, null)
            : new DashNotePlan(DashOutcome.Kept, Codex.Domain.Post.CallNote.For(number, note.Text.Trim(), now));
    }

    /// <summary>
    /// The window Post holds an act from the wrist for: the longer of the wrist's and the phone's (each the suite's one
    /// window, <see cref="FoldTiming"/>, on its own device), and <see cref="TransportGrace"/> for the undo to cross.
    /// </summary>
    public static TimeSpan HoldFor(TimeSpan watchWindow, TimeSpan phoneWindow)
    {
        var asked = watchWindow > DashLimits.MaxWindow ? DashLimits.MaxWindow : watchWindow;
        return FoldTiming.WindowFor(asked > phoneWindow ? asked : phoneWindow) + TransportGrace;
    }

    // ---- the rules underneath ----

    private DashActPlan Act(
        DashActKind kind, string id, string? key, IReadOnlyList<string> messages, TimeSpan watchWindow, DateTimeOffset now, TimeSpan phoneWindow,
        DashActs acts)
    {
        var window = HoldFor(watchWindow, phoneWindow);
        var syncAfter = kind == DashActKind.File ? SyncMoment.Delay(PostAction.Filed, window) : SyncMoment.Delay(PostAction.Deleted, window);
        if (acts.Knows(id))
        {
            return new DashActPlan(DashOutcome.Again, [], window, syncAfter);
        }

        var items = Targets(key, messages);
        if (items.Count == 0)
        {
            return new DashActPlan(DashOutcome.Unknown, [], window, syncAfter);
        }

        acts.Record(new DashActRecord(id, kind, items.Select(item => new ActedItem(item.Kind, item.SourceId, item.MessageId)).ToList(), now + window));
        return new DashActPlan(kind == DashActKind.File ? DashOutcome.Filed : DashOutcome.Held, items, window, syncAfter);
    }

    // A whole correspondence is what its row on the wrist showed (its texts, calls and voicemails, not its mail); a list of
    // messages is those of them still waiting, mail lines included.
    private IReadOnlyList<TimelineItem> Targets(string? key, IReadOnlyList<string> messages)
    {
        if (key is not null)
        {
            return Find(key) is { } thread ? Shown(thread) : [];
        }

        var named = messages.ToHashSet(StringComparer.Ordinal);
        return waiting.SelectMany(thread => thread.Items).Where(item => named.Contains(DashIds.Message(item.SourceId))).ToList();
    }

    private ContactThread? Find(string key) => waiting.FirstOrDefault(thread => DashIds.Correspondence(thread.ContactKey) == key);

    // What Dash shows of a correspondence: everything but its mail.
    private static IReadOnlyList<TimelineItem> Shown(ContactThread thread) =>
        thread.Items.Where(item => item.Kind != TimelineItemKind.Email).ToList();

    private DashPerson? Person(ContactThread thread)
    {
        var items = Shown(thread);
        if (items.Count == 0)
        {
            return null;
        }

        var latest = items[^1];
        return new DashPerson(
            DashIds.Correspondence(thread.ContactKey),
            Title(thread),
            Cut(OneLine(Words(latest)), DashLimits.Line),
            latest.TimestampUtc,
            snapshot.ReadState.IsUnread(thread with { Items = items }),
            thread.IsGroup);
    }

    private string Title(ContactThread thread) => Cut(ThreadTitles.ForRow(thread, snapshot.GroupNames, snapshot.NameOf), DashLimits.Name);

    private DashThreadMessage Message(ContactThread thread, TimelineItem item) =>
        new(DashIds.Message(item.SourceId),
            item.TimestampUtc,
            item.Incoming,
            TypeOf(item),
            Cut(Words(item), DashLimits.MessageText),
            thread.IsGroup && item.Incoming ? Cut(snapshot.NameOf(item.ContactKey), DashLimits.Name) : null,
            item.Attachments.Take(DashLimits.Attachments)
                .Select(attachment => new DashAttachment(DashIds.Attachment(item.SourceId, attachment.Id), TypeOf(attachment)))
                .ToList());

    // A text's own words (the Mailbox's line puts "Text:" in front; the wrist's row is the words themselves, as drawn),
    // "Photo" for a picture with none; a call, voicemail or note as the Mailbox describes it.
    private static string Words(TimelineItem item)
    {
        if (item.Kind != TimelineItemKind.Message)
        {
            return IsNote(item) ? (item.Snippet ?? string.Empty).Trim() : TimelineItemFormatter.Describe(item);
        }

        var words = (item.Snippet ?? string.Empty).Trim();
        return words.Length > 0 ? words
            : item.Attachments.Count > 0 ? AttachmentSummary.Describe(item.Attachments)
            : TimelineItemFormatter.Describe(item);
    }

    private static DashMessageType TypeOf(TimelineItem item) => item.Kind switch
    {
        TimelineItemKind.Call when IsNote(item) => DashMessageType.Note,
        TimelineItemKind.Call => DashMessageType.Call,
        TimelineItemKind.Voicemail => DashMessageType.Voicemail,
        _ => DashMessageType.Text,
    };

    private static DashAttachmentType TypeOf(AttachmentRef attachment) =>
        attachment.IsImage ? DashAttachmentType.Photo
        : attachment.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? DashAttachmentType.Video
        : DashAttachmentType.File;

    private static bool IsNote(TimelineItem item) => Codex.Domain.Post.CallNote.IsNote(item);

    // A number as typed or dictated: digits, with the plus, brackets, spaces, dots and hyphens people put between them.
    private static bool Dialable(string number) => number.Any(char.IsAsciiDigit) && DialableNumber().IsMatch(number);

    private static string OneLine(string text) => Spaces().Replace(text, " ").Trim();

    // At most the longest, cut at a whole character with an ellipsis to say so.
    private static string Cut(string text, int longest)
    {
        if (text.Length <= longest)
        {
            return text;
        }
        // Never half a surrogate pair: an emoji is cut whole or kept whole.
        var end = longest - 1;
        if (end > 0 && char.IsHighSurrogate(text[end - 1]))
        {
            end--;
        }
        return text[..end].TrimEnd() + "…";
    }

    private static DashSendPlan Refused(DashSendOutcome outcome) => new([], null, false, TextSendPath.Unavailable, outcome);

    [GeneratedRegex(@"^\+?[\d\s().\-]+$")]
    private static partial Regex DialableNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
