using System.Globalization;
using System.Text.Json;

namespace Index.Domain.Model;

/// <summary>One recorded balance: a loan's name as the sheet spelled it, the Friday, and the balance.</summary>
public sealed record SnowballHistoryRow(string Loan, DateOnly Week, decimal Balance);

/// <summary>
/// The owner's recorded Debt Snowball history as it travels from their Drive to the phone: the sheet's
/// weekly columns, its loan names, and one balance per (loan, week). The real figures live only in the
/// owner's own encrypted Drive file — never in the repository or the app — so this type is just the shape.
/// </summary>
public sealed record SnowballHistoryData(
    IReadOnlyList<DateOnly> Weeks, IReadOnlyList<string> Loans, IReadOnlyList<SnowballHistoryRow> Rows)
{
    /// <summary>The last week the history records. Anything the app writes itself is dated after it.</summary>
    public DateOnly LastWeek => Weeks.Max();
}

/// <summary>Why a history document was refused. Shape only: it names a position, never a value.</summary>
public sealed record SnowballHistoryParseResult(SnowballHistoryData? Data, string? Error)
{
    public bool Ok => Data is not null;
}

/// <summary>
/// Reads and validates the history JSON — the one place its shape is decided, shared by the Drive source
/// and the sealing tool so the two cannot drift. The shape is the sheet's "Loans Over Time" tab, one
/// balance per loan per week:
/// <code>{ "source": "...", "weeks": ["yyyy-MM-dd", ...], "loans": ["Name", ...],
///   "rows": [ { "loan": "Name", "week": "yyyy-MM-dd", "balance": 0.00 }, ... ] }</code>
/// Every row names a listed loan and a listed week, no (loan, week) pair repeats, and a balance is a
/// non-negative JSON number. A loan may have no row for a week it was not yet on the sheet.
///
/// <para>Never throws: a document that is not that shape is a result carrying a reason. The reason names a
/// position ("row 12"), never a loan or a figure, because it can end up in a log or a terminal.</para>
/// </summary>
public static class SnowballHistoryCodec
{
    private const string DateFormat = "yyyy-MM-dd";

    public static SnowballHistoryParseResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Refused("the document is empty");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return ParseRoot(document.RootElement);
        }
        catch (JsonException)
        {
            return Refused("the document is not valid JSON");
        }
    }

    private static SnowballHistoryParseResult ParseRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Refused("the document is not a JSON object");
        }

        if (!TryArray(root, "weeks", out var weekElements) || weekElements.GetArrayLength() == 0)
        {
            return Refused("\"weeks\" must be a non-empty array");
        }
        if (!TryArray(root, "loans", out var loanElements) || loanElements.GetArrayLength() == 0)
        {
            return Refused("\"loans\" must be a non-empty array");
        }
        if (!TryArray(root, "rows", out var rowElements) || rowElements.GetArrayLength() == 0)
        {
            return Refused("\"rows\" must be a non-empty array");
        }

        var weeks = new List<DateOnly>();
        var weekSet = new HashSet<DateOnly>();
        var weekIndex = 0;
        foreach (var element in weekElements.EnumerateArray())
        {
            weekIndex++;
            if (element.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(element.GetString(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var week))
            {
                return Refused($"week {weekIndex} is not a {DateFormat} date");
            }
            if (!weekSet.Add(week))
            {
                return Refused($"week {weekIndex} repeats an earlier week");
            }
            weeks.Add(week);
        }

        var loans = new List<string>();
        var loanSet = new HashSet<string>(StringComparer.Ordinal);
        var loanIndex = 0;
        foreach (var element in loanElements.EnumerateArray())
        {
            loanIndex++;
            var name = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return Refused($"loan {loanIndex} has no name");
            }
            if (!loanSet.Add(name))
            {
                return Refused($"loan {loanIndex} repeats an earlier loan");
            }
            loans.Add(name);
        }

        var rows = new List<SnowballHistoryRow>();
        var seen = new HashSet<(string, DateOnly)>();
        var rowIndex = 0;
        foreach (var element in rowElements.EnumerateArray())
        {
            rowIndex++;
            if (element.ValueKind != JsonValueKind.Object)
            {
                return Refused($"row {rowIndex} is not an object");
            }

            var loan = element.TryGetProperty("loan", out var loanProperty) && loanProperty.ValueKind == JsonValueKind.String
                ? loanProperty.GetString()
                : null;
            if (loan is null || !loanSet.Contains(loan))
            {
                return Refused($"row {rowIndex} names a loan that is not in \"loans\"");
            }

            if (!element.TryGetProperty("week", out var weekProperty)
                || weekProperty.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(weekProperty.GetString(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var week)
                || !weekSet.Contains(week))
            {
                return Refused($"row {rowIndex} names a week that is not in \"weeks\"");
            }

            if (!element.TryGetProperty("balance", out var balanceProperty)
                || balanceProperty.ValueKind != JsonValueKind.Number
                || !balanceProperty.TryGetDecimal(out var balance))
            {
                return Refused($"row {rowIndex} has no numeric balance");
            }
            if (balance < 0m)
            {
                return Refused($"row {rowIndex} has a negative balance");
            }

            if (!seen.Add((loan, week)))
            {
                return Refused($"row {rowIndex} repeats an earlier loan-and-week");
            }
            rows.Add(new SnowballHistoryRow(loan, week, balance));
        }

        return new SnowballHistoryParseResult(new SnowballHistoryData(weeks, loans, rows), null);
    }

    private static bool TryArray(JsonElement root, string name, out JsonElement array)
    {
        if (root.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }
        array = default;
        return false;
    }

    private static SnowballHistoryParseResult Refused(string reason) => new(null, "Not a Debt Snowball history: " + reason + ".");
}
