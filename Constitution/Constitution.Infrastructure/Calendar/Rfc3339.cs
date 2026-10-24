using System.Globalization;

namespace Constitution.Infrastructure.Calendar;

/// <summary>
/// An instant as Google Calendar's query parameters and event bodies want it: RFC 3339, with the offset as hours and minutes
/// (<c>2026-11-02T00:00:00-05:00</c>). The Google client writes a <c>DateTimeOffset</c> with the offset as hours alone (<c>-05</c>), which the
/// API's documentation does not promise to accept, so the adapters set the raw text instead. Culture-invariant.
/// </summary>
public static class Rfc3339
{
    private const string Pattern = "yyyy-MM-dd'T'HH:mm:sszzz";

    public static string Format(DateTimeOffset value) => value.ToString(Pattern, CultureInfo.InvariantCulture);
}
