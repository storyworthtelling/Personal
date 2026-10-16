using System.Globalization;
using System.Text.Json;
using Pulp.Domain.Core;
using Pulp.Domain.Services;

namespace Pulp.Domain.Usage;

/// <summary>
/// One service-month as the usage file in Drive holds it (D320): the broadcast's own fields — service, month,
/// count, last day opened. Typed, so nothing here can carry text (the usage shape guard holds that); the
/// merchant names go on the wire from the catalogue at write time and are not kept in memory.
/// </summary>
public sealed record IndexUsageEntry(ServiceId Service, UsageMonth Month, int Count, DateOnly? LastOpened);

/// <summary>
/// The file Pulp leaves for an Index install that Pulp never ran beside (D320). The broadcast reaches only the
/// phone both apps live on; this carries the same month totals through Drive, encrypted under the household
/// passphrase. Pure: merging a publish into what the file holds, and the plaintext's shape. Index's own codec
/// (<c>Ledger.Domain/Usage/PulpUsageFile.cs</c>) reads it, and a guard reads both by content to hold the names equal.
/// </summary>
public static class IndexUsageFile
{
    /// <summary>The address, not a description: Index finds the file by it.</summary>
    public const string FileName = "storyworthtelling-pulp-usage.json";

    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// The entries after a publish: each touched service-month replaces its earlier total, a count of zero removes
    /// it (the only hand-off of that month was undone), and everything else stays. Services the catalogue lacks
    /// are not written, as in the broadcast.
    /// </summary>
    public static IReadOnlyList<IndexUsageEntry> Apply(
        IReadOnlyList<IndexUsageEntry> existing, IEnumerable<IndexUsagePayload> payloads, ServiceCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(payloads);
        ArgumentNullException.ThrowIfNull(catalogue);
        var entries = existing.ToList();
        foreach (var payload in payloads)
        {
            if (catalogue.ById(payload.Service) is null)
            {
                continue;
            }

            entries.RemoveAll(entry => entry.Service.Equals(payload.Service) && entry.Month == payload.Month);
            if (payload.HandOffs > 0)
            {
                entries.Add(new IndexUsageEntry(payload.Service, payload.Month, payload.HandOffs, payload.LastOpened));
            }
        }

        return entries
            .OrderBy(entry => entry.Month.Year)
            .ThenBy(entry => entry.Month.Month)
            .ThenBy(entry => entry.Service.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The plaintext, with each service's merchant names from the catalogue; a service the catalogue lacks is left out, as in the broadcast.</summary>
    public static string Serialize(IReadOnlyList<IndexUsageEntry> entries, ServiceCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(catalogue);
        var rows = entries.Where(entry => catalogue.ById(entry.Service) is not null).Select(entry =>
        {
            var row = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["service"] = entry.Service.Value,
                ["month"] = entry.Month.ToString(),
                ["count"] = entry.Count,
                ["merchants"] = catalogue.ById(entry.Service)!.MerchantNames.ToList(),
            };
            if (entry.LastOpened is { } last)
            {
                row["lastOpened"] = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return row;
        }).ToList();
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["version"] = CurrentVersion, ["entries"] = rows }, Options);
    }

    /// <summary>Reads the plaintext, or says no. A file that is garbled, a different shape or a newer version is refused whole.</summary>
    public static bool TryRead(string? plaintext, out IReadOnlyList<IndexUsageEntry>? entries)
    {
        entries = null;
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(plaintext);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number) || number != CurrentVersion
                || !root.TryGetProperty("entries", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var read = new List<IndexUsageEntry>();
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object
                    || !Text(row, "service", out var service) || service.Length == 0
                    || !Text(row, "month", out var month)
                    || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedMonth)
                    || !row.TryGetProperty("count", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var total) || total < 0
                    || !row.TryGetProperty("merchants", out var merchants) || merchants.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                var names = merchants.EnumerateArray().Select(name => name.ValueKind == JsonValueKind.String ? name.GetString() : null).ToList();
                if (names.Count == 0 || names.Any(string.IsNullOrWhiteSpace))
                {
                    return false;
                }

                DateOnly? last = null;
                if (row.TryGetProperty("lastOpened", out _))
                {
                    if (!Text(row, "lastOpened", out var day)
                        || !DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    {
                        return false;
                    }

                    last = parsed;
                }

                read.Add(new IndexUsageEntry(new ServiceId(service), new UsageMonth(parsedMonth.Year, parsedMonth.Month), total, last));
            }

            entries = read;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool Text(JsonElement row, string name, out string value)
    {
        value = string.Empty;
        if (!row.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }
}
