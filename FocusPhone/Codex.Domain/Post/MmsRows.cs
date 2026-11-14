using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>One row of the provider's MMS part table, as the reader takes it.</summary>
public sealed record MmsStoredPart(
    string MessageId, string? PartId, string? ContentType, string? Text, bool HasFile,
    string? FileName, string? Name, string? ContentLocation);

/// <summary>One row of a message's address table: the address and its type (137 from, 151 to, 130 cc, 129 bcc).</summary>
public sealed record MmsStoredAddress(string? Address, string? Type);

/// <summary>A message's readable text and the parts it can offer to open.</summary>
public sealed record MmsContent(string? Text, IReadOnlyList<AttachmentRef> Attachments)
{
    public static readonly MmsContent Empty = new(null, []);
}

/// <summary>
/// Groups what the provider holds for MMS so the reader asks once for the whole part table, and once per message
/// for its addresses (efficiency audit §7). It used to ask three times for every message.
/// </summary>
public static class MmsRows
{
    private const string From = "137";
    private const string To = "151";
    private const int InboxBox = 1;

    // SMIL is the layout description every multi-part message carries, not something to show.
    private const string Smil = "application/smil";

    // The placeholder the provider writes for this phone's own address on a sent message.
    private const string AddressToken = "insert-address-token";

    public static IReadOnlyDictionary<string, MmsContent> GroupParts(IEnumerable<MmsStoredPart> parts)
    {
        var grouped = new Dictionary<string, (string? Text, List<AttachmentRef> Attachments)>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            if (!grouped.TryGetValue(part.MessageId, out var content))
            {
                content = (null, []);
            }

            var type = part.ContentType ?? string.Empty;
            if (string.Equals(type, "text/plain", StringComparison.OrdinalIgnoreCase))
            {
                // Text kept in a file is not streamed here; the mapper falls back to its placeholder.
                if (content.Text is null && !part.HasFile && part.Text is not null)
                {
                    content.Text = part.Text;
                }
            }
            else if (!type.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, Smil, StringComparison.OrdinalIgnoreCase)
                && type.Length > 0 && part.PartId is not null)
            {
                content.Attachments.Add(new AttachmentRef(
                    $"mms-part:{part.PartId}", FirstNonEmpty(part.FileName, part.Name, part.ContentLocation), type, SizeBytes: 0));
            }

            grouped[part.MessageId] = content;
        }

        return grouped.ToDictionary(entry => entry.Key, entry => new MmsContent(entry.Value.Text, entry.Value.Attachments), StringComparer.Ordinal);
    }

    public static MmsContent ContentOf(IReadOnlyDictionary<string, MmsContent> grouped, string messageId) =>
        grouped.TryGetValue(messageId, out var content) ? content : MmsContent.Empty;

    /// <summary>
    /// The message's other party (the sender of an incoming message, else the first recipient) and every address on
    /// it, from one read of its address rows.
    /// </summary>
    public static (string? Counterpart, List<string> All) Addresses(IEnumerable<MmsStoredAddress> rows, int messageBox)
    {
        var wanted = messageBox == InboxBox ? From : To;
        string? counterpart = null;
        var all = new List<string>();
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.Address) || row.Address == AddressToken)
            {
                continue;
            }
            all.Add(row.Address);
            if (counterpart is null && row.Type == wanted)
            {
                counterpart = row.Address;
            }
        }
        return (counterpart, all);
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrEmpty(value));
}
