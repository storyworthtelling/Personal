using System.Globalization;
using System.Text;
using FocusPhone.Domain.Time;

namespace FocusPhone.Domain.Diagnostics;

/// <summary>
/// Renders a <see cref="CrashReport"/> to a human-readable, round-trippable
/// text block and parses it back. The layout is both what the on-device crash
/// viewer shows and the on-disk file format, so it carries a machine-readable
/// timestamp (round-trip "O") alongside a friendly 12-hour line (DESIGN.md §9).
/// </summary>
public static class CrashLogFormatter
{
    private const string Header = "FocusPhone crash report";
    private const string MessageMarker = "--- Message ---";
    private const string StackMarker = "--- Stack trace ---";

    public static string Format(CrashReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Header);
        builder.AppendLine();
        builder.AppendLine($"App: {report.AppLabel}");
        builder.AppendLine($"Occurred: {TimeFormats.Stamp(report.When.DateTime)}");
        builder.AppendLine($"When: {report.When.ToString("O", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"Type: {report.ExceptionType}");
        builder.AppendLine();
        builder.AppendLine(MessageMarker);
        builder.AppendLine(report.Message);
        builder.AppendLine();
        builder.AppendLine(StackMarker);
        builder.Append(report.StackTrace);
        return builder.ToString();
    }

    public static bool TryParse(string text, out CrashReport? report)
    {
        report = null;
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith(Header, StringComparison.Ordinal))
        {
            return false;
        }

        var messageIndex = text.IndexOf(MessageMarker, StringComparison.Ordinal);
        var stackIndex = text.IndexOf(StackMarker, StringComparison.Ordinal);
        if (messageIndex < 0 || stackIndex < messageIndex)
        {
            return false;
        }

        var headerBlock = text[..messageIndex];
        var appLabel = FindField(headerBlock, "App:");
        var whenRaw = FindField(headerBlock, "When:");
        var type = FindField(headerBlock, "Type:");
        if (appLabel is null || whenRaw is null || type is null ||
            !DateTimeOffset.TryParse(whenRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var when))
        {
            return false;
        }

        var message = text[(messageIndex + MessageMarker.Length)..stackIndex].Trim('\r', '\n');
        var stack = text[(stackIndex + StackMarker.Length)..].Trim('\r', '\n');

        report = new CrashReport(when, appLabel, type, message, stack);
        return true;
    }

    private static string? FindField(string block, string label)
    {
        foreach (var line in block.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith(label, StringComparison.Ordinal))
            {
                return trimmed[label.Length..].Trim();
            }
        }
        return null;
    }
}
