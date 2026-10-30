namespace Codex.Domain.Post;

/// <summary>
/// What was actually said in an MMS's WAP-push notification: enough to ask the carrier
/// for the message itself. This is not the MMS — it is the doorbell.
/// </summary>
public sealed record MmsNotification(string TransactionId, string ContentLocation)
{
    /// <summary>When the carrier stops holding the message, in Unix seconds; null when it did not say.</summary>
    public long? ExpirySeconds { get; init; }
}

/// <summary>
/// Parses the WAP push PDU Android hands the WAP_PUSH_DELIVER receiver's "data" extra — an
/// M-Notification.ind (OMA-WAP-209-MMS-Encapsulation) — for what a download needs: the
/// transaction id, the content location (the URL to fetch the real message from), and the
/// expiry.
///
/// It reads every header through the same AOSP-shaped reader as a downloaded message
/// (<see cref="MmsRetrieveConfParser"/>). The first version walked only the fields it
/// expected and stopped at any other — a subject line, a delivery-report flag, a recipient,
/// a carrier's own header — and since Content-Location comes last, every real notice that
/// carried one of them was dropped before its download was ever asked for. On the owner's
/// phone that was every MMS for weeks.
/// </summary>
public static class MmsNotificationParser
{
    private const int FieldMessageType = 0x8C;
    private const int MessageTypeNotificationInd = 0x82;

    public static MmsNotification? Parse(byte[]? pdu) => Parse(pdu, DateTimeOffset.UtcNow);

    /// <param name="receivedAt">What a relative expiry counts from.</param>
    public static MmsNotification? Parse(byte[]? pdu, DateTimeOffset receivedAt)
    {
        if (pdu is null || pdu.Length == 0)
        {
            return null;
        }
        // The notice proper starts at Message-Type = notification-ind. On the owner's phone
        // something comes before it (every real notice read as a text header first), so
        // each place that pair appears is tried in turn; it cannot occur inside a text run.
        for (var start = 0; start < pdu.Length - 1; start++)
        {
            if (pdu[start] == FieldMessageType && pdu[start + 1] == MessageTypeNotificationInd
                && ParseAt(start == 0 ? pdu : pdu[start..], receivedAt) is { } notification)
            {
                return notification;
            }
        }
        return null;
    }

    private static MmsNotification? ParseAt(byte[] pdu, DateTimeOffset receivedAt)
    {
        var headers = MmsRetrieveConfParser.ReadHeaders(pdu);
        if (headers?.MessageType != MessageTypeNotificationInd
            || string.IsNullOrEmpty(headers.TransactionId)
            || string.IsNullOrEmpty(headers.ContentLocation))
        {
            return null;
        }
        long? expiry = headers.Expiry switch
        {
            { Relative: true } relative => receivedAt.ToUnixTimeSeconds() + relative.Value,
            { } absolute => absolute.Value,
            null => null,
        };
        return new MmsNotification(headers.TransactionId, headers.ContentLocation) { ExpirySeconds = expiry };
    }
}

/// <summary>
/// A PDU's layout, for the log when one will not read: its length, its message type and the
/// header field codes in the order met. Codes only — never a number, an address, a subject
/// or a URL — so the line can be shared.
/// </summary>
public static class MmsPduShape
{
    private const int FieldMessageType = 0x8C;

    public static int? MessageType(byte[] pdu) =>
        pdu.Length >= 2 && pdu[0] == FieldMessageType ? pdu[1] : null;

    public static string Describe(byte[] pdu)
    {
        var seen = new List<string>();
        var headers = MmsRetrieveConfParser.ReadHeaders(pdu, seen);
        var type = MessageType(pdu) is { } value ? $"0x{value:X2}" : "unknown";
        var line = $"{pdu.Length} bytes; type {type}; fields {string.Join(' ', seen)}";
        return headers is null ? line + "; ends early" : line;
    }

    /// <summary>
    /// The PDU's bytes with every run of printable text replaced by its length, and every
    /// other octet in hex: enough to see what wraps a notice and where it begins, with no
    /// number, address, subject or link left in it.
    /// </summary>
    public static string Redacted(byte[] pdu, int limit = 96)
    {
        var parts = new List<string>();
        var at = 0;
        var end = Math.Min(pdu.Length, limit);
        while (at < end)
        {
            var run = 0;
            while (at + run < end && pdu[at + run] is >= 0x20 and <= 0x7E)
            {
                run++;
            }
            if (run > 0)
            {
                parts.Add($"<text {run}>");
                at += run;
                continue;
            }
            parts.Add(pdu[at].ToString("X2"));
            at++;
        }
        var line = string.Join(' ', parts);
        return pdu.Length > limit ? $"{line} … ({pdu.Length} bytes)" : line;
    }
}
