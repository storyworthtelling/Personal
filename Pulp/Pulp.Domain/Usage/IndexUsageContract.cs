using System.Globalization;
using Pulp.Domain.Core;
using Pulp.Domain.Services;

namespace Pulp.Domain.Usage;

/// <summary>
/// What crosses to Index for one service in one calendar month (D90, D269–D271): the service id, the
/// month, how many times it was opened through Pulp, the last day it was, and the merchant names the
/// service's charge appears under (public catalogue data, so Index keeps no second table to drift).
/// Minutes watched, titles and viewers have no field here, and no decision allows one.
/// </summary>
public sealed record IndexUsagePayload
{
    public IndexUsagePayload(ServiceId service, UsageMonth month, int handOffs, DateOnly? lastOpened)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(handOffs);
        Service = service;
        Month = month;
        HandOffs = handOffs;
        LastOpened = lastOpened;
    }

    public ServiceId Service { get; }

    public UsageMonth Month { get; }

    public int HandOffs { get; }

    public DateOnly? LastOpened { get; }

    public static IndexUsagePayload From(ServiceMonthUsage row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new IndexUsagePayload(row.Service, row.Month, row.HandOffs, row.LastOpened);
    }

    /// <summary>Months with at least one hand-off, for services the table knows. Minutes-only months say nothing Index counts.</summary>
    public static IReadOnlyList<IndexUsagePayload> ForRows(IEnumerable<ServiceMonthUsage> rows, ServiceCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(catalogue);
        var payloads = new List<IndexUsagePayload>();
        foreach (var row in rows.Where(row => row.HandOffs > 0))
        {
            if (catalogue.ById(row.Service) is not null)
            {
                payloads.Add(From(row));
            }
        }

        return payloads;
    }
}

/// <summary>
/// The Pulp to Index usage contract (G16), the Post to Index balance pattern again: an explicit
/// broadcast naming Index's receiver, guarded by a signature permission Index declares. Index kept
/// the Ledger package id, so the package below is the same one Post's <c>IndexHandoff</c> names. The
/// count crosses as exact text. There is no outbox: Index absent is the ordinary case, and Pulp sends
/// the month's totals again on the next hand-off. Index's own receiver and manifest are checked
/// against these literals by content, on both sides' tests.
/// </summary>
public static class IndexUsageContract
{
    public const string Package = "com.storyworthtelling.ledger";
    public const string Receiver = "com.storyworthtelling.ledger.UsageReceiver";
    public const string Action = "com.storyworthtelling.ledger.action.USAGE";
    public const string Permission = "com.storyworthtelling.ledger.RECEIVE_USAGE";

    public const string ServiceExtra = "service";
    public const string MonthExtra = "month";
    public const string CountExtra = "count";
    public const string LastOpenedExtra = "lastOpened";
    public const string MerchantsExtra = "merchants";

    /// <summary>Joins merchant names in one text extra; a name containing it is refused rather than split.</summary>
    public const char MerchantSeparator = '|';

    /// <summary>The payload as intent extras, with the merchant names the catalogue gives the service. A service the catalogue lacks cannot be sent.</summary>
    public static IReadOnlyDictionary<string, string> Extras(IndexUsagePayload payload, ServiceCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(catalogue);
        var service = catalogue.ById(payload.Service)
            ?? throw new ArgumentException($"Service '{payload.Service}' is not in the catalogue, so Index could not match its charge.", nameof(payload));
        if (service.MerchantNames.Any(name => name.Contains(MerchantSeparator, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"A merchant name for '{payload.Service}' contains '{MerchantSeparator}', which would split it in two on arrival.",
                nameof(payload));
        }

        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ServiceExtra] = payload.Service.Value,
            [MonthExtra] = payload.Month.ToString(),
            [CountExtra] = payload.HandOffs.ToString(CultureInfo.InvariantCulture),
            [MerchantsExtra] = string.Join(MerchantSeparator, service.MerchantNames),
        };
        if (payload.LastOpened is { } last)
        {
            extras[LastOpenedExtra] = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return extras;
    }
}
