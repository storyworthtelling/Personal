using System.Globalization;

namespace Index.Domain.Usage;

/// <summary>
/// One service's month as Pulp sends it (Pulp D90, D269–D271): the service id, the calendar month, how
/// many times the service was opened through Pulp, the last day it was, and the merchant names its
/// charge appears under. Nothing else crosses — no title, no viewer, no minutes. The field names and
/// separator below are Pulp's contract, held equal by <c>PulpUsageContractTests</c> (G16).
/// </summary>
public sealed record PulpUsageReceipt(
    string Service, string YearMonth, int Count, DateOnly? LastOpened, IReadOnlyList<string> Merchants)
{
    public const char MerchantSeparator = '|';

    /// <summary>
    /// Reads a receipt through <paramref name="extra"/> (an intent extra lookup). A receipt missing or
    /// garbling the service, month, count or merchants is refused whole rather than guessed; a missing
    /// or unreadable last-opened day only means "no date".
    /// </summary>
    public static bool TryParse(Func<string, string?> extra, out PulpUsageReceipt? receipt)
    {
        ArgumentNullException.ThrowIfNull(extra);
        receipt = null;

        var service = extra("service")?.Trim();
        var month = extra("month");
        var merchants = (extra("merchants") ?? string.Empty)
            .Split(MerchantSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (string.IsNullOrEmpty(service) || merchants.Length == 0 ||
            !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            !int.TryParse(extra("count"), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            return false;
        }

        DateOnly? last = DateOnly.TryParseExact(
            extra("lastOpened"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

        receipt = new PulpUsageReceipt(service, month!, count, last, merchants);
        return true;
    }
}
