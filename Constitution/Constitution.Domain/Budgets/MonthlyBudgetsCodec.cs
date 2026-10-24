using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Constitution.Domain.Budgets;

/// <summary>
/// The one JSON text the latest-per-month budgets are kept as. The property names are frozen (a phone already holding the file
/// reads it by them), and an amount is a JSON <b>string</b>, never a number: a number is read as a double by half the tools
/// that will ever open this file, and the figure is the text.
/// </summary>
public static class MonthlyBudgetsCodec
{
    private const int Version = 1;

    public static string Encode(MonthlyBudgets budgets)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("months");
            foreach (var entry in budgets.Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("month", entry.Month);
                writer.WriteString("budget", BudgetPayload.FormatAmount(entry.Budget));
                writer.WriteString("spent", BudgetPayload.FormatAmount(entry.Spent));
                writer.WriteString("receivedAt", entry.ReceivedAt.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <exception cref="FormatException">The text is not a record of the budgets this app can read, with the reason.</exception>
    public static MonthlyBudgets Decode(string text)
    {
        try
        {
            using var parsed = JsonDocument.Parse(text);
            var root = parsed.RootElement;
            if (root.GetProperty("version").GetInt32() != Version)
            {
                throw new FormatException("This record is of a version this app does not read.");
            }

            var entries = root.GetProperty("months").EnumerateArray().Select(Read).ToList();
            return MonthlyBudgets.From(entries);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw new FormatException("The record could not be read as the month-by-month grocery budgets.", e);
        }
    }

    private static BudgetEntry Read(JsonElement item)
    {
        var month = Text(item, "month");
        if (!BudgetPayload.IsMonth(month))
        {
            throw new FormatException("The record holds a month that is not of the form yyyy-MM.");
        }

        return new BudgetEntry(
            month,
            Amount(item, "budget"),
            Amount(item, "spent"),
            DateTimeOffset.ParseExact(Text(item, "receivedAt"), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    private static decimal Amount(JsonElement item, string name) =>
        BudgetPayload.TryParseAmount(Text(item, name), out var amount)
            ? amount
            : throw new FormatException($"The record holds an amount in '{name}' that is not exact decimal text.");

    // GetString on a number throws InvalidOperationException, which Decode turns into a FormatException: a figure written as a
    // JSON number is refused rather than read through a double.
    private static string Text(JsonElement item, string name) =>
        item.GetProperty(name).GetString() ?? throw new FormatException($"The record holds no text for '{name}'.");
}
