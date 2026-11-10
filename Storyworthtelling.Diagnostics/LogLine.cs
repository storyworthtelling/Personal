using System.Globalization;
using System.Text;

namespace Storyworthtelling.Diagnostics;

/// <summary>
/// The one shape an entry is written in, native and MAUI alike: <c>[stamp] LEVEL Source — message</c>, then any detail
/// lines. The MAUI file logger wrote <c>[stamp] [LEVEL] Source: message</c>, which the log loop's reader did not
/// recognise, so a whole MAUI segment read as one line (audit 08 §1). The reader still accepts that older shape for
/// segments already sealed on a phone.
/// </summary>
public static class LogLine
{
    /// <summary>
    /// The file stamp: ISO-8601 to the millisecond with the phone's offset (audit 08 §25). A minute-only stamp with no
    /// year or zone could not order a tap, an action and a crash inside one minute, nor be matched to an Exit line.
    /// </summary>
    public static string Stamp(DateTimeOffset moment) =>
        moment.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

    public static string Format(DateTimeOffset stamp, string level, string source, string message, string? detail)
    {
        var builder = new StringBuilder();
        builder.Append('[').Append(Stamp(stamp)).Append("] ")
            .Append(level).Append(' ').Append(source).Append(" — ").Append(message);
        if (!string.IsNullOrEmpty(detail))
        {
            builder.Append('\n').Append(detail);
        }
        return builder.Append('\n').ToString();
    }
}
