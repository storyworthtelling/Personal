using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>
/// Post's own alert for an arriving text: the thread a tap opens, the title and line it
/// shows, how many new messages it covers, and the newest message's time, which becomes
/// the conversation's new watermark.
/// </summary>
public sealed record TextAlert(string ThreadKey, string Title, string Line, int Count, DateTimeOffset HeardThrough);

/// <summary>
/// Decides when an arriving text alerts. Google Messages holds the SMS role and is kept off
/// Flyleaf's shelf, and the locked phone's disabled status bar turns off every app's
/// notification sound, vibration and heads-up card — so a text announced only by Messages'
/// own notification arrives with nothing to say so. Post therefore raises its own
/// notification (the bookmark on its book) and buzzes itself, from the Messages
/// notification its listener already reads. Messages re-posts a conversation's notification
/// for every change, a reboot included, so only an incoming message newer than the last one
/// alerted for that conversation counts; with nothing remembered, only one from the last
/// few minutes.
/// </summary>
public static class TextAlerts
{
    /// <summary>How recent a message must be to alert when its conversation has never alerted.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(10);

    /// <summary>The buzz: two short pulses, as off/on milliseconds from the start.</summary>
    public static readonly long[] BuzzPattern = [0, 250, 150, 250];

    // not copy: a key prefix for the remembered watermark, never shown.
    private const string TitlePrefix = "title:";

    /// <summary>
    /// The conversation a watermark is kept under: Messages' own conversation id (the
    /// notification's shortcut id), else its title. Null when there is neither.
    /// </summary>
    public static string? ConversationOf(MessagingNotice notice)
    {
        if (!string.IsNullOrWhiteSpace(notice.ConversationId))
        {
            return notice.ConversationId;
        }
        return string.IsNullOrWhiteSpace(notice.Title) ? null : TitlePrefix + notice.Title.Trim();
    }

    /// <summary>The alert this notice warrants, or null when it holds nothing new from anyone else.</summary>
    public static TextAlert? Decide(
        MessagingNotice notice, Func<string, string> contactKeyFor, DateTimeOffset? heardThrough, DateTimeOffset now)
    {
        var since = heardThrough ?? now - Fresh;
        var fresh = notice.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.SenderName))
            .Where(message => DateTimeOffset.FromUnixTimeMilliseconds(message.TimestampMs) > since)
            .OrderBy(message => message.TimestampMs)
            .ToList();
        if (fresh.Count == 0)
        {
            return null;
        }

        var latest = fresh[^1];
        var item = MessagingNoticeMapper.Map(notice with { Messages = [latest] }, contactKeyFor)[0];
        var threadKey = item.GroupId is null ? item.ContactKey : TimelineMerger.GroupThreadKey(item.GroupId);

        var sender = latest.SenderName!.Trim();
        var words = string.IsNullOrWhiteSpace(latest.Text) ? "New message" : latest.Text.Trim();
        var title = notice.IsGroup
            ? MessagingNoticeMapper.GroupTitle(notice) ?? "Group"
            : sender;
        var line = notice.IsGroup ? $"{sender}: {words}" : words;

        return new TextAlert(threadKey, title, line, fresh.Count,
            DateTimeOffset.FromUnixTimeMilliseconds(latest.TimestampMs));
    }
}
