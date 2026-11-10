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
    public static string Format(DateTimeOffset stamp, string level, string source, string message, string? detail)
    {
        var builder = new StringBuilder();
        builder.Append('[').Append(stamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append("] ")
            .Append(level).Append(' ').Append(source).Append(" — ").Append(message);
        if (!string.IsNullOrEmpty(detail))
        {
            builder.Append('\n').Append(detail);
        }
        return builder.Append('\n').ToString();
    }
}
