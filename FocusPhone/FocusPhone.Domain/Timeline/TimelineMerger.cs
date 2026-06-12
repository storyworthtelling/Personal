namespace FocusPhone.Domain.Timeline;

/// <summary>
/// Merges call-log, message, and email items into per-contact threads:
/// items deduplicated by (kind, source id), chronological within a thread,
/// threads ordered by most recent activity.
/// </summary>
public class TimelineMerger
{
    public IReadOnlyList<ContactThread> Merge(IEnumerable<TimelineItem> items)
    {
        return items
            .DistinctBy(item => (item.Kind, item.SourceId))
            .GroupBy(item => item.ContactKey)
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.TimestampUtc).ToList();
                return new ContactThread(group.Key, ordered, ordered[^1].TimestampUtc);
            })
            .OrderByDescending(thread => thread.LastActivityUtc)
            .ToList();
    }
}
