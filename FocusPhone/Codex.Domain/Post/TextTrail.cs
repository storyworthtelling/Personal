namespace Codex.Domain.Post;

/// <summary>
/// The lines Post writes to its texts trail — one per text that reaches it, by whichever
/// route — so a late, misfiled or missing text can be traced after the fact on a phone with
/// no logcat. The trail is meant to be shared, so it records how and when, never what was
/// said or who said it: a sender is the last two digits of their number, and a notification
/// is counted rather than quoted.
///
/// Lateness is the gap between when the sender's phone sent a message and when this phone
/// had it. A text another phone tried as RCS first, then gave up on, shows here as a
/// notification or SMS marked LATE.
/// </summary>
public static class TextTrail
{
    /// <summary>A gap longer than this is marked LATE; ordinary SMS takes seconds.</summary>
    public static readonly TimeSpan LateAfter = TimeSpan.FromMinutes(2);

    private const string LateMark = " — LATE";

    public static string Sms(string? address, DateTimeOffset sentAt, DateTimeOffset receivedAt, int parts) =>
        $"SMS from {Sender(address)}, {Count(parts, "part")}; sent {Lag(receivedAt - sentAt)} before it reached the phone"
        + Late(receivedAt - sentAt);

    public static string Notice(MessagingNotice notice, DateTimeOffset shownAt)
    {
        var shape = notice.IsGroup
            ? $"group conversation ({Count(SenderCount(notice), "sender")})"
            : "one-to-one";
        var line = $"Google Messages notification: {shape}, {Count(notice.Messages.Count, "message")}";
        if (notice.Messages.Count == 0)
        {
            return line;
        }
        var newest = DateTimeOffset.FromUnixTimeMilliseconds(notice.Messages.Max(message => message.TimestampMs));
        return $"{line}; newest sent {Lag(shownAt - newest)} before it was shown" + Late(shownAt - newest);
    }

    public static string MmsAnnounced() => "MMS announced by the carrier; download requested";

    /// <summary>An MMS's address list includes the owner, so more than two addresses is a group.</summary>
    public static string MmsDownloaded(int addresses) =>
        addresses > 2 ? $"MMS downloaded: group of {addresses} addresses" : "MMS downloaded: one-to-one";

    public static string Lag(TimeSpan gap)
    {
        if (gap < TimeSpan.FromMinutes(1))
        {
            return "under a minute";
        }
        if (gap < TimeSpan.FromHours(1))
        {
            return $"{(int)gap.TotalMinutes} min";
        }
        if (gap < TimeSpan.FromDays(1))
        {
            return gap.Minutes == 0 ? $"{(int)gap.TotalHours} h" : $"{(int)gap.TotalHours} h {gap.Minutes} min";
        }
        return gap.Hours == 0 ? $"{(int)gap.TotalDays} d" : $"{(int)gap.TotalDays} d {gap.Hours} h";
    }

    private static string Late(TimeSpan gap) => gap > LateAfter ? LateMark : string.Empty;

    private static int SenderCount(MessagingNotice notice) =>
        notice.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.SenderName))
            .Select(message => message.SenderName)
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static string Sender(string? address)
    {
        var digits = new string((address ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 2 ? $"…{digits[^2..]}" : "an unknown sender";
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
