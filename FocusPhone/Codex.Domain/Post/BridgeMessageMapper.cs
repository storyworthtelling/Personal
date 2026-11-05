using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>
/// Turns a Google Messages message (from the bridge) into a Mailbox item.
///
/// <para>A person's texts thread on their number, normalized the way calls and provider texts
/// are, so a text, a call and a voicemail from one person meet in one correspondence. A group
/// threads on Messages' own conversation id (<c>gm:&lt;id&gt;</c>): Messages already decides
/// who is in a conversation, so Post does not rebuild it from an address list. Members are
/// everyone but the owner, as keys for matching and as the original spellings for dialing.</para>
///
/// <para>The item id is <c>gm:&lt;message id&gt;</c>, which is what the delete path reads back
/// to delete in Messages.</para>
/// </summary>
public static class BridgeMessageMapper
{
    public const string SourcePrefix = "gm:";

    public const string GroupPrefix = "gm:";

    private const string AttachmentPrefix = "gm-media:";

    public static TimelineItem? Map(BridgeMessage message, BridgeConversation? conversation)
    {
        var attachments = message.Media
            .Select(media => new AttachmentRef(AttachmentId(media), media.Name, media.MimeType, media.Size))
            .ToList();
        var text = message.Text.Trim();
        if (text.Length == 0 && attachments.Count == 0)
        {
            return null;
        }
        var snippet = text.Length > 0 ? text : AttachmentSummary.Describe(attachments);
        if (message.Failed)
        {
            snippet = "Not sent: " + snippet;
        }

        var others = conversation?.Participants.Where(p => !p.IsMe && p.Number.Length > 0).ToList() ?? [];
        var isGroup = conversation is not null && (conversation.IsGroup || others.Count > 1);
        var contactKey = ContactKeyFor(message, conversation, others, isGroup);

        var item = new TimelineItem(
            TimelineItemKind.Message,
            contactKey,
            BridgeTimestamp.ToInstant(message.Timestamp),
            snippet,
            Incoming: !message.FromMe,
            SourceId: SourcePrefix + message.MessageId)
        {
            ExternalConversationId = message.ConversationId,
            Unread = !message.FromMe && !message.IsOld,
            Attachments = attachments,
        };
        if (!isGroup)
        {
            return item;
        }

        // One key per person, keeping the first spelling seen, so the two lists line up.
        var keys = new List<string>();
        var addresses = new List<string>();
        foreach (var participant in others)
        {
            var key = ContactKeyNormalizer.NormalizeAny(participant.Number);
            if (!keys.Contains(key))
            {
                keys.Add(key);
                addresses.Add(participant.Number);
            }
        }
        return item with
        {
            GroupId = GroupPrefix + message.ConversationId,
            Participants = keys,
            ParticipantAddresses = addresses,
        };
    }

    private static string ContactKeyFor(
        BridgeMessage message, BridgeConversation? conversation, IReadOnlyList<BridgeParticipant> others, bool isGroup)
    {
        if (conversation is null)
        {
            // An event can beat its conversation; the conversation itself is the only key there is.
            return GroupPrefix + message.ConversationId;
        }
        if (!message.FromMe)
        {
            var sender = conversation.Participants.FirstOrDefault(p => p.Id == message.ParticipantId);
            if (sender is { Number.Length: > 0 })
            {
                return ContactKeyNormalizer.NormalizeAny(sender.Number);
            }
        }
        if (!isGroup && others.Count == 1)
        {
            return ContactKeyNormalizer.NormalizeAny(others[0].Number);
        }
        return GroupPrefix + message.ConversationId;
    }

    /// <summary>An attachment id that carries what the download needs: the media id and its key.</summary>
    public static string AttachmentId(BridgeMedia media) => $"{AttachmentPrefix}{media.MediaId}|{media.Key}";

    /// <summary>The media id and key back out of an attachment id, or null for an id from elsewhere.</summary>
    public static (string MediaId, string Key)? ReadAttachmentId(string attachmentId)
    {
        if (!attachmentId.StartsWith(AttachmentPrefix, StringComparison.Ordinal))
        {
            return null;
        }
        var body = attachmentId[AttachmentPrefix.Length..];
        var bar = body.IndexOf('|');
        return bar <= 0 ? null : (body[..bar], body[(bar + 1)..]);
    }
}
