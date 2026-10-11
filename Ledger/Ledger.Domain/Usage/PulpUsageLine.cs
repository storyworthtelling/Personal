using System.Globalization;

namespace Index.Domain.Usage;

/// <summary>
/// The one muted line under a streaming charge (mockup 15; Pulp D269/D270): "Opened 14 times in Pulp"
/// for the calendar month, or "Not opened in Pulp since Jun 28" once a service has gone two calendar
/// months unopened. A fact in the same muted ink — it carries no colour, so nothing here chooses one.
/// A charge Pulp does not know gets no line, and neither does a service Pulp never reported an opening
/// for: the line never implies a count it was not told.
/// </summary>
public static class PulpUsageLine
{
    public const int MonthsUnusedBeforeSince = 2;

    public static string? For(PulpUsageLedger ledger, string merchantName, string viewedYearMonth, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (string.IsNullOrWhiteSpace(merchantName))
        {
            return null;
        }

        var name = merchantName.Trim();
        var entries = ledger.Entries
            .Where(entry => entry.Merchants.Any(merchant => string.Equals(merchant, name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (entries.Count == 0)
        {
            return null;
        }

        var thisMonth = entries.FirstOrDefault(entry => entry.YearMonth == viewedYearMonth);
        if (thisMonth is { Count: > 0 })
        {
            return thisMonth.Count == 1
                ? "Opened 1 time in Pulp"
                : string.Create(CultureInfo.InvariantCulture, $"Opened {thisMonth.Count} times in Pulp");
        }

        if (viewedYearMonth != $"{today.Year:D4}-{today.Month:D2}")
        {
            return null;
        }

        var last = entries.Where(entry => entry.Count > 0 && entry.LastOpened is not null)
            .Select(entry => entry.LastOpened!.Value)
            .DefaultIfEmpty()
            .Max();
        if (last == default)
        {
            return null;
        }

        var monthsAgo = (today.Year - last.Year) * 12 + today.Month - last.Month;
        return monthsAgo >= MonthsUnusedBeforeSince
            ? "Not opened in Pulp since " + last.ToString("MMM d", CultureInfo.InvariantCulture)
            : null;
    }
}
