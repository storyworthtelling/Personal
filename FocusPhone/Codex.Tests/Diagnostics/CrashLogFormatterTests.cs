using Codex.Domain.Diagnostics;
using Xunit;

namespace Codex.Tests.Diagnostics;

public class CrashLogFormatterTests
{
    private static CrashReport Sample() => new(
        new DateTimeOffset(2026, 7, 10, 14, 30, 0, TimeSpan.FromHours(-4)),
        "FocusPhone",
        "System.InvalidOperationException",
        "Font asset not packaged: fonts/SourceSerif4-Regular.ttf",
        "   at Codex.Ui.PaperTheme.Load(Context context, String assetPath)\n   at Flyleaf.HomeActivity.OnCreate(Bundle state)");

    [Fact]
    public void Format_ContainsAppTypeMessageAndStack()
    {
        var text = CrashLogFormatter.Format(Sample());

        Assert.Contains("FocusPhone", text);
        Assert.Contains("System.InvalidOperationException", text);
        Assert.Contains("Font asset not packaged: fonts/SourceSerif4-Regular.ttf", text);
        Assert.Contains("at Flyleaf.HomeActivity.OnCreate", text);
    }

    [Fact]
    public void Format_IncludesFriendlyTwelveHourTime()
    {
        // DESIGN.md §9: list/detail timestamps are 12-hour, lowercase meridiem.
        var text = CrashLogFormatter.Format(Sample());

        Assert.Contains("2:30 pm", text);
    }

    [Fact]
    public void FormatThenParse_RoundTrips()
    {
        var original = Sample();

        var ok = CrashLogFormatter.TryParse(CrashLogFormatter.Format(original), out var report);

        Assert.True(ok);
        Assert.NotNull(report);
        Assert.Equal(original.When, report!.When);
        Assert.Equal(original.AppLabel, report.AppLabel);
        Assert.Equal(original.ExceptionType, report.ExceptionType);
        Assert.Equal(original.Message, report.Message);
        Assert.Equal(original.StackTrace, report.StackTrace);
    }

    [Fact]
    public void TryParse_Garbage_ReturnsFalse()
    {
        Assert.False(CrashLogFormatter.TryParse("not a crash report", out var report));
        Assert.Null(report);
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsFalse()
    {
        Assert.False(CrashLogFormatter.TryParse("", out _));
    }
}
