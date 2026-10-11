using System.Text.Json;

namespace Index.Domain.Usage;

/// <summary>
/// Every month Pulp has told Index about, one entry per service and month. Pulp sends a month's running
/// total each time, so the latest receipt for a service and month replaces the earlier one. Immutable;
/// <see cref="Apply"/> returns the next ledger.
/// </summary>
public sealed class PulpUsageLedger
{
    public static PulpUsageLedger Empty { get; } = new([]);

    public PulpUsageLedger(IReadOnlyList<PulpUsageReceipt> entries) => Entries = entries;

    public IReadOnlyList<PulpUsageReceipt> Entries { get; }

    public PulpUsageLedger Apply(PulpUsageReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var kept = Entries.Where(entry => !Same(entry, receipt)).Append(receipt)
            .OrderBy(entry => entry.YearMonth, StringComparer.Ordinal)
            .ThenBy(entry => entry.Service, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PulpUsageLedger(kept);
    }

    private static bool Same(PulpUsageReceipt left, PulpUsageReceipt right) =>
        string.Equals(left.Service, right.Service, StringComparison.OrdinalIgnoreCase) && left.YearMonth == right.YearMonth;
}

/// <summary>The ledger as stored on the phone. A missing or garbled value reads as an empty ledger: usage is a convenience, never a reason to fail.</summary>
public static class PulpUsageCodec
{
    public static string Encode(PulpUsageLedger ledger) => JsonSerializer.Serialize(ledger.Entries);

    public static PulpUsageLedger Decode(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return PulpUsageLedger.Empty;
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<PulpUsageReceipt>>(stored);
            return entries is null || entries.Any(entry => entry is null || entry.Merchants is null)
                ? PulpUsageLedger.Empty
                : new PulpUsageLedger(entries);
        }
        catch (JsonException)
        {
            return PulpUsageLedger.Empty;
        }
    }
}
