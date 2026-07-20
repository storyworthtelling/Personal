using FocusPhone.Domain.Diagnostics;
using Xunit;

namespace FocusPhone.Tests.Diagnostics;

public class DiagnosticsReportTests
{
    private static DiagnosticsContext Context() => new(
        "Communication",
        "1.4.0",
        "Pixel 8a",
        new DateTimeOffset(2026, 7, 10, 14, 30, 0, TimeSpan.FromHours(-4)));

    private static CrashReport Crash() => new(
        new DateTimeOffset(2026, 7, 9, 8, 0, 0, TimeSpan.Zero),
        "Communication",
        "System.InvalidOperationException",
        "SQLite provider missing",
        "   at FocusPhone.Communication.TimelineDatabase.Open()");

    [Fact]
    public void Build_ContainsAllSectionsAndSnapshot()
    {
        var text = DiagnosticsReport.Build(Context(), "some log text", lastCrash: null);

        Assert.Contains("Communication", text);
        Assert.Contains("1.4.0", text);
        Assert.Contains("Pixel 8a", text);
        Assert.Contains("DIAGNOSTIC LOG", text);
        Assert.Contains("some log text", text);
        Assert.Contains("LAST CRASH", text);
    }

    [Fact]
    public void Build_WithCrash_IncludesCrashDetail()
    {
        var text = DiagnosticsReport.Build(Context(), "log", Crash());

        Assert.Contains("SQLite provider missing", text);
        Assert.Contains("System.InvalidOperationException", text);
    }

    [Fact]
    public void Build_NoCrash_NotesNoCrashRecorded()
    {
        var text = DiagnosticsReport.Build(Context(), "log", lastCrash: null);

        Assert.Contains("no crash", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_EmptyLog_NotesNoEntries()
    {
        var text = DiagnosticsReport.Build(Context(), "   ", lastCrash: null);

        Assert.Contains("no entries", text, StringComparison.OrdinalIgnoreCase);
    }
}
