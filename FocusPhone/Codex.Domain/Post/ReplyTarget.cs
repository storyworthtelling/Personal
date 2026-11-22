using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>
/// Where a text in a correspondence goes (P14 B1, out of the Mailbox's open correspondence): the numbers it is sent to and
/// the Google Messages conversation the bridge replies into. A person is texted on the first number Post holds for them;
/// a group on its members' own spelling of their numbers (the normalized ones when an older cache has no other); a
/// group known only from notifications is not offered a reply at all, since it names only who has spoken.
/// </summary>
public sealed record ReplyTarget(IReadOnlyList<string> Recipients, string? ConversationId, bool ToGroup)
{
    /// <summary>A key that is a phone number: every character a digit.</summary>
    public static bool IsPhone(string key) => key.Length > 0 && key.All(char.IsAsciiDigit);

    /// <summary>The number a person's correspondence is called and texted on.</summary>
    public static string? PhoneOf(IReadOnlyList<string> memberKeys, string contactKey) =>
        memberKeys.FirstOrDefault(IsPhone) ?? (IsPhone(contactKey) ? contactKey : null);

    /// <summary>Everyone a group reply goes to.</summary>
    public static IReadOnlyList<string> GroupRecipients(IReadOnlyList<string> addresses, IReadOnlyList<string> participants) =>
        addresses.Count > 0 ? addresses : participants;

    /// <summary>Whether Post knows everyone in the groups these items belong to (<see cref="GroupMembership"/>).</summary>
    public static bool MembershipKnown(IEnumerable<TimelineItem> items) =>
        items.Where(item => item.IsGroup).All(item => GroupMembership.IsKnown(item.GroupId));

    /// <summary>The conversation the bridge replies into: the latest item's that carries one.</summary>
    public static string? ConversationOf(IEnumerable<TimelineItem> items) =>
        items.LastOrDefault(item => item.ExternalConversationId is not null)?.ExternalConversationId;

    /// <summary>The whole target for a correspondence, or null when a reply has nowhere to go.</summary>
    public static ReplyTarget? For(ContactThread thread)
    {
        var conversation = ConversationOf(thread.Items);
        if (thread.IsGroup)
        {
            var recipients = GroupRecipients(thread.ParticipantAddresses, thread.Participants);
            return recipients.Count > 0 && MembershipKnown(thread.Items) ? new ReplyTarget(recipients, conversation, ToGroup: true) : null;
        }
        return PhoneOf(thread.MemberKeys, thread.ContactKey) is { } phone ? new ReplyTarget([phone], conversation, ToGroup: false) : null;
    }
}
