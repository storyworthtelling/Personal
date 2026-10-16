using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>One message as a Google Messages notification lists it. A null sender is the owner.</summary>
public sealed record NoticeMessage(string? SenderName, long TimestampMs, string? Text);

/// <summary>
/// A Google Messages notification, reduced to what Post reads from it: the conversation's
/// id (the notification's shortcut id), its title, whether Messages calls it a group, and
/// the messages it lists. The head builds this from the notification's MessagingStyle; the
/// rules for turning it into timeline items live here so they can be tested.
/// </summary>
public sealed record MessagingNotice(
    string? ConversationId,
    string? Title,
    bool IsGroup,
    IReadOnlyList<NoticeMessage> Messages);

/// <summary>
/// Turns a Google Messages notification into timeline items. While the bridge is unpaired
/// this is the only route an RCS message has into Post, and it used to key every message on
/// its sender — so a group conversation landed as a scatter of one-to-one threads, one per
/// member who spoke. A group now threads on its conversation instead.
/// </summary>
public static class MessagingNoticeMapper
{
    /// <summary>The <see cref="TimelineItem.GroupId"/> prefix for a group known only from a notification.</summary>
    public const string GroupPrefix = "rcs:";

    public static IReadOnlyList<TimelineItem> Map(MessagingNotice notice, Func<string, string> contactKeyFor)
    {
        var groupId = GroupIdFor(notice);
        var titleKey = string.IsNullOrWhiteSpace(notice.Title) ? null : contactKeyFor(notice.Title);

        var senders = notice.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.SenderName))
            .Select(message => contactKeyFor(message.SenderName!))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var items = new List<TimelineItem>();
        foreach (var message in notice.Messages)
        {
            var fromOwner = string.IsNullOrWhiteSpace(message.SenderName);
            // The owner's own message is keyed on who it went to: the other person in a
            // one-to-one, the group itself in a group.
            var contactKey = fromOwner ? (groupId ?? titleKey) : contactKeyFor(message.SenderName!);
            if (contactKey is null)
            {
                continue;
            }

            items.Add(new TimelineItem(
                TimelineItemKind.Message,
                contactKey,
                DateTimeOffset.FromUnixTimeMilliseconds(message.TimestampMs),
                message.Text,
                Incoming: !fromOwner,
                // The id this listener has always used, so a message already cached under it
                // is updated in place — and moved into its group — rather than doubled.
                SourceId: $"{contactKey}:{message.TimestampMs}")
            {
                GroupId = groupId,
                // Only the members who have spoken, which is why ParticipantAddresses stays
                // empty: this is not the group, and nothing may be sent to it as if it were.
                Participants = groupId is null ? [] : senders,
            });
        }
        return items;
    }

    /// <summary>The conversation's own name, when it is a group — offered as a title until the owner names it.</summary>
    public static string? GroupTitle(MessagingNotice notice) =>
        notice.IsGroup && !string.IsNullOrWhiteSpace(notice.Title) ? notice.Title.Trim() : null;

    // The shortcut id is Messages' own conversation id, so it survives a rename; the title
    // is the fallback. With neither, nothing stable exists to thread on, and a key invented
    // per notification would split one group into many.
    private static string? GroupIdFor(MessagingNotice notice)
    {
        if (!notice.IsGroup)
        {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(notice.ConversationId))
        {
            return GroupPrefix + notice.ConversationId;
        }
        return string.IsNullOrWhiteSpace(notice.Title) ? null : $"{GroupPrefix}title:{notice.Title.Trim()}";
    }
}

/// <summary>Whether Post knows everyone in a group, and so may send to it.</summary>
public static class GroupMembership
{
    /// <summary>
    /// True for a group read from the provider, whose address list is the whole group. A
    /// group known only from notifications lists just the members who have spoken, so a
    /// group MMS to it would leave people out without anyone noticing.
    /// </summary>
    public static bool IsKnown(string? groupId) =>
        groupId is not null && !groupId.StartsWith(MessagingNoticeMapper.GroupPrefix, StringComparison.Ordinal);
}
