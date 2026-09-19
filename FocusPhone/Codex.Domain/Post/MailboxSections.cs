using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>
/// Splits marketing-classified mail out of the ordinary Mailbox into its own visible
/// bucket. This is a triage split, not a new exception to "everything that arrived and is
/// not yet dealt with" — moving marketing mail to its own bucket is itself a form of
/// dealt-with, the way filing or blocking are. It does not decide when marketing mail
/// goes; <see cref="MailAgeing"/> still owns that, on the same one-day lifetime as before.
/// A message the reading pass has not yet judged (<see cref="TimelineItem.MailClass"/> is
/// null) stays in the ordinary Mailbox, exactly as it always has.
/// </summary>
public static class MailboxSections
{
    public static (List<TimelineItem> Ordinary, List<TimelineItem> Marketing) SplitMarketing(
        IEnumerable<TimelineItem> items)
    {
        var ordinary = new List<TimelineItem>();
        var marketing = new List<TimelineItem>();
        foreach (var item in items)
        {
            var bucket = item.MailClass == MailClass.Marketing ? marketing : ordinary;
            bucket.Add(item);
        }
        return (ordinary, marketing);
    }
}
