using System.Text;
using Codex.Domain.Time;

namespace Codex.Domain.Diagnostics;

/// <summary>
/// Device and app facts for the exported diagnostics bundle. Kept Android-free so
/// <see cref="DiagnosticsReport"/> stays in Domain and unit-testable; the head
/// fills these from <c>PackageInfo</c> / <c>Android.OS.Build</c>.
/// </summary>
public record DiagnosticsContext(
    string AppLabel,
    string AppVersion,
    string DeviceModel,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Assembles the shareable "export diagnostics" text: a snapshot header, the full
/// rolling <see cref="DiagnosticLog"/> contents, and the latest captured crash
/// (reusing <see cref="CrashLogFormatter"/>). Modeled on Storyboarder's export
/// bundle so a single file carries everything needed to diagnose off-device.
/// </summary>
public static class DiagnosticsReport
{
    public static string Build(DiagnosticsContext context, string diagnosticLog, CrashReport? lastCrash)
    {
        var builder = new StringBuilder();
        builder.AppendLine("===== SNAPSHOT =====");
        builder.AppendLine($"App: {context.AppLabel}");
        builder.AppendLine($"Version: {context.AppVersion}");
        builder.AppendLine($"Device: {context.DeviceModel}");
        builder.AppendLine($"Generated: {TimeFormats.Stamp(context.GeneratedAt.DateTime)}");
        builder.AppendLine();

        builder.AppendLine("===== DIAGNOSTIC LOG =====");
        builder.AppendLine(string.IsNullOrWhiteSpace(diagnosticLog) ? "(no entries)" : diagnosticLog.TrimEnd());
        builder.AppendLine();

        builder.AppendLine("===== LAST CRASH =====");
        builder.Append(lastCrash is null ? "(no crash recorded)" : CrashLogFormatter.Format(lastCrash));
        return builder.ToString();
    }
}
