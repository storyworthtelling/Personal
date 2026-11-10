using System.Globalization;
using Storyworthtelling.Diagnostics;

namespace LogLoopReader;

/// <summary>
/// When an entry was written, from its own stamp (audit 08 §3). Every head now stamps ISO-8601 with the phone's offset
/// (<c>2026-10-30T09:15:05.250-04:00</c>, audit 08 §25), read exactly. Older segments still carry the native
/// <c>Oct 30, 9:15 AM</c> (no year, no zone) or the MAUI <c>2026-10-30 09:15:00</c>; both are the phone's local clock, read
/// here as UTC, which is close enough to order a week and to see a worker that stopped. A stamp that cannot be read, or that falls outside
/// the batch it came in, gives way to the batch's own range.
/// </summary>
public static class EntryTime
{
    private static readonly string[] Shapes =
    [
        "yyyy-MM-dd'T'HH:mm:ss.fffzzz", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-dd HH:mm:ss zzz",
    ];

    private static readonly string[] YearlessShapes = ["MMM d, h:mm tt", "MMM d, h:mm:ss tt", "MMM d, HH:mm"];

    public static DateTimeOffset? Read(string stamp, LogBatchHeader header)
    {
        if (string.IsNullOrWhiteSpace(stamp))
        {
            return null;
        }
        var text = stamp.Trim();
        DateTimeOffset? read = null;
        if (DateTimeOffset.TryParseExact(text, Shapes, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var full))
        {
            read = full;
        }
        else if (DateTime.TryParseExact(text, YearlessShapes, CultureInfo.InvariantCulture, DateTimeStyles.None, out var yearless))
        {
            // The year is the batch's; a December stamp in a batch that ends in January belongs to the year before.
            var year = header.LastUtc.Year;
            var candidate = new DateTimeOffset(year, yearless.Month, yearless.Day, yearless.Hour, yearless.Minute, yearless.Second, TimeSpan.Zero);
            if (candidate > header.LastUtc.AddDays(1))
            {
                candidate = candidate.AddYears(-1);
            }
            read = candidate;
        }

        // A day either side covers the phone's zone; anything further is not this batch's.
        return read is { } when && when >= header.FirstUtc.AddDays(-1) && when <= header.LastUtc.AddDays(1) ? when : null;
    }
}
