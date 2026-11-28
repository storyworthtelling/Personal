using Codex.Domain.Timeline;
using Watch.Domain.Dash;

namespace Codex.Infrastructure.Dash;

/// <summary>
/// Post telling the wrist a text arrived (P14 B8; WD §7, A12): the one message Post sends the wrist unasked. Where the
/// phone itself alerts for an arriving text (a spam text never does), Post names its correspondence as People waiting
/// names it (<see cref="DashIds.Correspondence"/> of a group's thread key, or of a person's own key, which is the first of
/// their keys People waiting shows a merged person under whenever a text comes from their number), says when it came, and
/// whether whoever sent it is a favourite, starred in the phone's own contacts. Never a name, a number or a word. Pure:
/// the head reads the star, and tells a wrist in reach once.
/// </summary>
public static class DashArrivals
{
    public static DashArrived For(TimelineItem arriving, Func<string, bool> isStarred) =>
        new(DashIds.Correspondence(arriving.GroupId is { } group ? TimelineMerger.GroupThreadKey(group) : arriving.ContactKey),
            arriving.TimestampUtc,
            !string.IsNullOrWhiteSpace(arriving.ContactKey) && isStarred(arriving.ContactKey));
}
