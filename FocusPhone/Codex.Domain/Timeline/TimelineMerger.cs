namespace Codex.Domain.Timeline;

/// <summary>
/// Merges call-log, voicemail, message, and email items into threads: items
/// deduplicated by (kind, source id), cross-source duplicates collapsed,
/// chronological within a thread, threads ordered by most recent activity.
///
/// A person's items are grouped by a resolved identity (so their texts, calls,
/// and emails share one thread); group conversations are grouped by their
/// GroupId instead. The parameterless overload groups strictly by ContactKey —
/// the behavior when no contact/identity data is available.
/// </summary>
public class TimelineMerger
{
    public IReadOnlyList<ContactThread> Merge(IEnumerable<TimelineItem> items) =>
        Merge(items, PerKeyIdentityResolver.Instance);

    public IReadOnlyList<ContactThread> Merge(IEnumerable<TimelineItem> items, IIdentityResolver resolver)
    {
        return NearDuplicateCollapser.Collapse(
                items.DistinctBy(item => (item.Kind, item.SourceId)).ToList())
            .GroupBy(item => ThreadKeyOf(item, resolver))
            .Select(group => BuildThread(group.Key, group, resolver))
            .OrderByDescending(thread => thread.LastActivityUtc)
            .ToList();
    }

    private static string ThreadKeyOf(TimelineItem item, IIdentityResolver resolver) =>
        item.GroupId is { } groupId ? GroupThreadKey(groupId) : resolver.ResolveThreadKey(item.ContactKey);

    private static string GroupThreadKey(string groupId) => "group:" + groupId;

    private static ContactThread BuildThread(string threadKey, IEnumerable<TimelineItem> group, IIdentityResolver resolver)
    {
        var ordered = group.OrderBy(item => item.TimestampUtc).ToList();
        var isGroup = ordered[0].IsGroup;
        var memberKeys = ordered
            .Where(item => !item.IsGroup)
            .Select(item => item.ContactKey)
            .Distinct()
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        var participants = ordered
            .SelectMany(item => item.Participants)
            .Distinct()
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        // Deduplicated by normalized key again here, not just carried over from each item:
        // two messages in the same thread can each have picked a different first-seen raw
        // spelling for the same person, and this is the one place membership is unioned
        // across all of them.
        var participantAddresses = ordered
            .SelectMany(item => item.ParticipantAddresses)
            .GroupBy(ContactKeyNormalizer.NormalizePhone)
            .Select(group => group.First())
            .OrderBy(address => address, StringComparer.Ordinal)
            .ToList();

        // Representative key: the resolved identity for a person, or the group id.
        var representative = isGroup ? threadKey : (memberKeys.Count > 0 ? memberKeys[0] : ordered[0].ContactKey);

        return new ContactThread(representative, ordered, ordered[^1].TimestampUtc)
        {
            MemberKeys = memberKeys,
            Participants = participants,
            ParticipantAddresses = participantAddresses,
            IsGroup = isGroup,
            HasUnread = ordered.Any(item => item.Unread),
        };
    }
}
