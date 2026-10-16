using System.Globalization;
using System.Text.Json;

namespace Index.Domain.Usage;

/// <summary>
/// Reads the usage file Pulp leaves in the household's Drive (Pulp D320): the same service-month totals the
/// broadcast carries, for the install that shows a charge on a phone Pulp never ran beside. The file name,
/// version and field names are Pulp's (<c>Pulp/Pulp.Domain/Usage/IndexUsageFile.cs</c>), held equal by
/// <c>PulpUsageFileGuardTests</c>. A file that is garbled, a different shape or a newer version reads as
/// nothing — never a guess — and the caller keeps what it already had.
/// </summary>
public static class PulpUsageFile
{
    public const string FileName = "storyworthtelling-pulp-usage.json";

    public const int CurrentVersion = 1;

    /// <summary>The ledger the plaintext describes, or null when it is not a usage file this version can read.</summary>
    public static PulpUsageLedger? Decode(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(plaintext);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != CurrentVersion
                || !root.TryGetProperty("entries", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var ledger = PulpUsageLedger.Empty;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object
                    || Text(row, "service") is not { Length: > 0 } service
                    || Text(row, "month") is not { } month
                    || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    || !row.TryGetProperty("count", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var total) || total < 0
                    || !row.TryGetProperty("merchants", out var merchants) || merchants.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var names = merchants.EnumerateArray()
                    .Select(name => name.ValueKind == JsonValueKind.String ? name.GetString() : null)
                    .ToList();
                if (names.Count == 0 || names.Any(string.IsNullOrWhiteSpace))
                {
                    return null;
                }

                DateOnly? last = null;
                if (row.TryGetProperty("lastOpened", out _))
                {
                    if (!DateOnly.TryParseExact(Text(row, "lastOpened"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    {
                        return null;
                    }

                    last = day;
                }

                ledger = ledger.Apply(new PulpUsageReceipt(service, month, total, last, names!));
            }

            return ledger;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
