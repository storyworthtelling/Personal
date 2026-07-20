using System.Text;
using FocusPhone.Domain.Time;

namespace FocusPhone.Domain.Diagnostics;

/// <summary>
/// Renders a <see cref="DiagnosticEntry"/> to a human-readable block for the
/// on-device diagnostics screen and the on-disk <see cref="DiagnosticLog"/>. The
/// header line carries a friendly 12-hour timestamp (DESIGN.md §9); any captured
/// exception is written verbatim on the following lines so a stack trace survives
/// for diagnosis.
/// </summary>
public static class DiagnosticLogFormatter
{
    public static string FormatEntry(DiagnosticEntry entry)
    {
        var builder = new StringBuilder();
        builder.Append('[').Append(TimeFormats.Stamp(entry.When.DateTime)).Append("] ");
        builder.Append(entry.Level).Append(' ').Append(entry.Source).Append(" — ").Append(entry.Message);
        if (!string.IsNullOrEmpty(entry.Detail))
        {
            builder.Append('\n').Append(entry.Detail);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Builds an entry whose detail is the exception rendered in full (type,
    /// message, and stack), or no detail when <paramref name="exception"/> is null.
    /// </summary>
    public static DiagnosticEntry FromException(
        DateTimeOffset when, string level, string source, string message, Exception? exception) =>
        new(when, level, source, message, exception?.ToString());
}
