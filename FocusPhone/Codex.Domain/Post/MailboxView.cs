using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>
/// What the Mailbox shows: every correspondence waiting, and marketing mail in its own section, each merged by person
/// and stamped read or unread. Built here once, out of the Mailbox's own reload (P14 B1), so the phone's Mailbox and
/// the watch (Dash, through Post's relay) are shown the same correspondences by the same rule: nothing hidden, nothing
/// the owner judged spam, and nothing inside a delete's undo window.
/// </summary>
public sealed record MailboxView(IReadOnlyList<ContactThread> Waiting, IReadOnlyList<ContactThread> Marketing)
{
    public static MailboxView Build(
        IEnumerable<TimelineItem> items,
        IReadOnlySet<(TimelineItemKind Kind, string SourceId)> hidden,
        SpamList spam,
        DeleteBuffer deletes,
        IIdentityResolver resolver,
        ReadState readState)
    {
        var shown = items.Where(item => !hidden.Contains((item.Kind, item.SourceId)));
        var visible = deletes.Filter(spam.Filter(shown)).ToList();
        var (ordinary, marketing) = MailboxSections.SplitMarketing(visible);
        return new MailboxView(Threads(ordinary, resolver, readState), Threads(marketing, resolver, readState));
    }

    private static IReadOnlyList<ContactThread> Threads(IEnumerable<TimelineItem> items, IIdentityResolver resolver, ReadState readState) =>
        new TimelineMerger().Merge(items, resolver).Select(readState.Apply).ToList();
}
