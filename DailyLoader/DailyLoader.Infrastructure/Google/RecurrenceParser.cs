using DailyLoader.Domain.Models;

namespace DailyLoader.Infrastructure.Google;

// Parses a Google event's RRULE recurrence lines into the frequency and occurrence count the
// outlook selector needs. Kept separate from the calendar client so it can be unit-tested.
public static class RecurrenceParser
{
    public static (RecurrenceFrequency Frequency, int? Count) Parse(IEnumerable<string>? recurrence)
    {
        if (recurrence is null)
            return (RecurrenceFrequency.None, null);

        foreach (var line in recurrence)
        {
            if (line is null || !line.StartsWith("RRULE", StringComparison.OrdinalIgnoreCase))
                continue;

            var frequency = RecurrenceFrequency.None;
            int? count = null;

            foreach (var part in line.Split(';'))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2)
                    continue;
                var key = kv[0].Trim();
                var value = kv[1].Trim();

                if (key.EndsWith("FREQ", StringComparison.OrdinalIgnoreCase))
                    frequency = MapFrequency(value);
                else if (key.Equals("COUNT", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(value, out var parsed))
                    count = parsed;
            }

            return (frequency, count);
        }

        return (RecurrenceFrequency.None, null);
    }

    private static RecurrenceFrequency MapFrequency(string value) =>
        value.ToUpperInvariant() switch
        {
            "DAILY" => RecurrenceFrequency.Daily,
            "WEEKLY" => RecurrenceFrequency.Weekly,
            "MONTHLY" => RecurrenceFrequency.Monthly,
            "YEARLY" => RecurrenceFrequency.Yearly,
            _ => RecurrenceFrequency.None
        };
}
