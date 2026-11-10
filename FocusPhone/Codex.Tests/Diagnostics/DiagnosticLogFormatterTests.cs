using Codex.Domain.Diagnostics;
using Xunit;

namespace Codex.Tests.Diagnostics;

public class DiagnosticLogFormatterTests
{
    private static DiagnosticEntry Sample(string? detail = null) => new(
        new DateTimeOffset(2026, 7, 10, 14, 30, 0, TimeSpan.FromHours(-4)),
        DiagnosticLevel.Warning,
        "EmailSync",
        "Email sync failed; will retry",
        detail);

    [Fact]
    public void FormatEntry_ContainsLevelSourceAndMessage()
    {
        var line = DiagnosticLogFormatter.FormatEntry(Sample());

        Assert.Contains(DiagnosticLevel.Warning, line);
        Assert.Contains("EmailSync", line);
        Assert.Contains("Email sync failed; will retry", line);
    }

    [Fact]
    public void FormatEntry_StampsTheFileInIsoWithItsOffset()
    {
        // A minute-only stamp with no year or zone could not order a tap, an action and a crash inside one minute, nor
        // match an Exit line (audit 08 §25). The file is for the log loop; the screen keeps its friendly time.
        var line = DiagnosticLogFormatter.FormatEntry(Sample());

        Assert.StartsWith("[2026-07-10T14:30:00.000-04:00] ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatEntry_WithDetail_IncludesDetailOnFollowingLine()
    {
        var line = DiagnosticLogFormatter.FormatEntry(Sample("System.Net.Sockets.SocketException: refused"));

        Assert.Contains("System.Net.Sockets.SocketException: refused", line);
        Assert.Contains('\n', line);
    }

    [Fact]
    public void FormatEntry_NoDetail_IsSingleLine()
    {
        var line = DiagnosticLogFormatter.FormatEntry(Sample());

        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void FromException_CapturesTypeAndMessageAsDetail()
    {
        var exception = new InvalidOperationException("bridge not paired");

        var entry = DiagnosticLogFormatter.FromException(
            Sample().When, DiagnosticLevel.Error, "Bridge", "Startup failed", exception);

        Assert.Equal(DiagnosticLevel.Error, entry.Level);
        Assert.NotNull(entry.Detail);
        Assert.Contains("System.InvalidOperationException", entry.Detail!);
        Assert.Contains("bridge not paired", entry.Detail!);
    }

    [Fact]
    public void FromException_NullException_LeavesDetailNull()
    {
        var entry = DiagnosticLogFormatter.FromException(
            Sample().When, DiagnosticLevel.Warning, "Bridge", "Something odd", null);

        Assert.Null(entry.Detail);
    }
}
