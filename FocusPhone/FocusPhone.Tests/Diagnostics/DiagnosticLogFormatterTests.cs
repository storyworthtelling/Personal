using FocusPhone.Domain.Diagnostics;
using Xunit;

namespace FocusPhone.Tests.Diagnostics;

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
    public void FormatEntry_IncludesFriendlyTwelveHourTime()
    {
        // DESIGN.md §9: detail timestamps are 12-hour, lowercase meridiem.
        var line = DiagnosticLogFormatter.FormatEntry(Sample());

        Assert.Contains("2:30 pm", line);
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
