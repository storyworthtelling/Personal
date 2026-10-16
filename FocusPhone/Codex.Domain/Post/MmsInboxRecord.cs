namespace Codex.Domain.Post;

/// <summary>One row of an MMS's address table: who, and in which role (from, to, cc).</summary>
public sealed record MmsAddressRow(string Address, int Type);

/// <summary>
/// One row of an MMS's part table. Text and SMIL are stored in the row's own text column —
/// the provider refuses a file for them — and everything else is <see cref="Data"/>, written
/// to the file the provider makes for the part.
/// </summary>
public sealed record MmsPartRow(
    string ContentType,
    int Charset,
    string? Name,
    string? ContentLocation,
    string? ContentId,
    string? Text,
    byte[]? Data);

/// <summary>
/// A downloaded MMS laid out as the provider's three tables hold it: the message row, its
/// addresses, its parts. Mirrors what AOSP's PduPersister writes for a received message, so
/// the record Post keeps is the one any other messaging app would read.
/// </summary>
public sealed record MmsInboxRecord(
    long DateSeconds,
    long? DateSentSeconds,
    string? Subject,
    string? MessageId,
    string? TransactionId,
    string? ContentType,
    int? MmsVersion,
    bool TextOnly,
    IReadOnlyList<MmsAddressRow> Addresses,
    IReadOnlyList<MmsPartRow> Parts)
{
    // Telephony.Mms.Addr type values, as PduHeaders numbers the fields.
    public const int AddressFrom = 137;
    public const int AddressTo = 151;
    public const int AddressCc = 130;

    /// <summary>The message type column for a received message (M-Retrieve.conf).</summary>
    public const int MessageTypeRetrieveConf = 132;

    private const string TextPlain = "text/plain";
    private const string Smil = "application/smil";
    private const string TextHtml = "text/html";

    /// <summary>
    /// Dated when it reached this phone, like an SMS, with the sender's own date kept as the
    /// date it was sent — so a message that was slow to arrive sits where the owner first
    /// saw it rather than hours back.
    /// </summary>
    public static MmsInboxRecord From(RetrievedMms message, DateTimeOffset receivedAt)
    {
        var addresses = new List<MmsAddressRow>();
        if (message.From is not null)
        {
            addresses.Add(new MmsAddressRow(message.From, AddressFrom));
        }
        addresses.AddRange(message.To.Select(address => new MmsAddressRow(address, AddressTo)));
        addresses.AddRange(message.Cc.Select(address => new MmsAddressRow(address, AddressCc)));

        var parts = message.Parts.Select(ToRow).ToList();
        var textOnly = parts.Count <= 2 && parts.All(part => part.ContentType is TextPlain or Smil);

        return new MmsInboxRecord(
            receivedAt.ToUnixTimeSeconds(),
            message.DateSeconds,
            message.Subject,
            message.MessageId,
            message.TransactionId,
            message.ContentType,
            message.MmsVersion,
            textOnly,
            addresses,
            parts);
    }

    /// <summary>Every text part, in order — what the message says, for a notification.</summary>
    public string? Text
    {
        get
        {
            var texts = Parts
                .Where(part => part.ContentType == TextPlain && !string.IsNullOrWhiteSpace(part.Text))
                .Select(part => part.Text!)
                .ToList();
            return texts.Count == 0 ? null : string.Join("\n", texts);
        }
    }

    private static MmsPartRow ToRow(RetrievedPart part)
    {
        var contentType = part.ContentType.Trim().ToLowerInvariant();
        if (contentType == "image/jpg")
        {
            contentType = "image/jpeg";
        }
        var inline = contentType is TextPlain or Smil or TextHtml;
        return new MmsPartRow(
            contentType,
            // Inline text is decoded here and stored as UTF-8, so its charset says so.
            inline ? MmsCharsets.Utf8 : part.Charset,
            part.Name,
            part.ContentLocation,
            part.ContentId,
            inline ? part.Text() : null,
            inline ? null : part.Data);
    }
}

/// <summary>A placeholder row a download left behind: the carrier's notice, not yet the message.</summary>
public sealed record MmsStub(long Id, string? TransactionId, string? ContentLocation, long DateSeconds, long? ExpirySeconds);

/// <summary>
/// Which stuck downloads to ask for again. A download fails for ordinary reasons — no
/// mobile data at that moment, the carrier slow to answer — and the carrier keeps the message
/// until it expires, so a failure is not the end of it. Nothing retries on its own otherwise:
/// the carrier's notice came once.
/// </summary>
public static class MmsStubRetry
{
    public const int MaxAttempts = 3;

    /// <summary>A download younger than this is likely still in flight.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMinutes(5);

    /// <summary>How long to keep trying when the carrier gave no expiry.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(7);

    public static IReadOnlyList<MmsStub> Due(
        IEnumerable<MmsStub> stubs, IReadOnlyDictionary<long, int> attempts, DateTimeOffset now)
    {
        var nowSeconds = now.ToUnixTimeSeconds();
        return stubs
            .Where(stub => !string.IsNullOrEmpty(stub.ContentLocation) && !string.IsNullOrEmpty(stub.TransactionId))
            .Where(stub => attempts.GetValueOrDefault(stub.Id) < MaxAttempts)
            .Where(stub => nowSeconds - stub.DateSeconds >= (long)Settle.TotalSeconds)
            .Where(stub => stub.ExpirySeconds is { } expiry
                ? expiry > nowSeconds
                : nowSeconds - stub.DateSeconds <= (long)Horizon.TotalSeconds)
            .ToList();
    }
}
