namespace Foreword.Domain.Delivery;

/// <summary>The documents a morning sends to the tablet, each delivered at most once a day.</summary>
public enum MorningDocument
{
    Brief,
    Bonus,
    Day,
    Prayer,
}

/// <summary>
/// What has reached the tablet today (audit 11 §7). The reMarkable upload always makes a new document, so a retry after
/// the brief landed — or after a timeout the server had in fact accepted — put a second "Morning Brief" on the tablet.
/// A retry asks this first and sends only what is missing. It is one line in preferences, "yyyy-MM-dd|Brief,Day", and a
/// line from another day counts as nothing delivered.
/// </summary>
public sealed class MorningLedger
{
    private readonly DateOnly date;
    private readonly HashSet<MorningDocument> delivered;

    private MorningLedger(DateOnly date, HashSet<MorningDocument> delivered)
    {
        this.date = date;
        this.delivered = delivered;
    }

    public static MorningLedger Read(string? stored, DateOnly today)
    {
        var documents = new HashSet<MorningDocument>();
        var parts = (stored ?? string.Empty).Split('|');
        if (parts.Length == 2
            && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)
            && day == today)
        {
            foreach (var name in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<MorningDocument>(name, out var document) && Enum.IsDefined(document))
                {
                    documents.Add(document);
                }
            }
        }
        return new MorningLedger(today, documents);
    }

    public bool Delivered(MorningDocument document) => delivered.Contains(document);

    public MorningLedger With(MorningDocument document) => new(date, [.. delivered, document]);

    public string Write() =>
        $"{date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}|{string.Join(",", delivered.Order())}";
}
