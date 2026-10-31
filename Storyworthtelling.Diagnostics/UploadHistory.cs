using System.Globalization;

namespace Storyworthtelling.Diagnostics;

/// <summary>
/// What the weekly upload worker remembers between runs (LOG-LOOP-PLAN.md §3.3): when an upload last read
/// back and matched (the input to <see cref="UploadWindow.Due"/>), and how many upload windows in a row
/// failed (the input to <see cref="LogUploadStatus.Sentence"/>). Weeks are counted by window, so seven
/// failed daily runs in one week are one failed week.
/// </summary>
public sealed record UploadHistory(DateTimeOffset? LastConfirmed, DateTimeOffset? LastFailedWindow, int FailedWeeks)
{
    public static UploadHistory None { get; } = new(null, null, 0);

    public UploadHistory Confirmed(DateTimeOffset now) => new(now, null, 0);

    public UploadHistory Failed(DateTimeOffset now)
    {
        var window = UploadWindow.Start(now);
        return LastFailedWindow is { } last && last == window
            ? this
            : this with { LastFailedWindow = window, FailedWeeks = FailedWeeks + 1 };
    }

    /// <summary>One line for a preferences string.</summary>
    public string Format() => string.Join('|',
        LastConfirmed?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        LastFailedWindow?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        FailedWeeks.ToString(CultureInfo.InvariantCulture));

    /// <summary>A missing or garbled value is no history: the next run simply uploads.</summary>
    public static UploadHistory Parse(string? stored)
    {
        var parts = stored?.Split('|');
        if (parts is not { Length: 3 } || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var weeks))
        {
            return None;
        }
        return new UploadHistory(Time(parts[0]), Time(parts[1]), weeks);
    }

    private static DateTimeOffset? Time(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value) ? value : null;
}
