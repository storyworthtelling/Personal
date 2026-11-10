namespace RepoGuards.Tests;

/// <summary>
/// Every MAUI head sends every crash route to the uploaded log (audit 08 §19): the Java thread's uncaught handler, the
/// managed unhandled exception and a faulted task nobody awaited. Folio and Foreword had no unobserved-task hook, and
/// Index sent two of the three only to files the upload never carried.
/// </summary>
public class MauiCrashHooksTests
{
    [Theory]
    [InlineData("Ledger/Ledger.Android/Platforms/Android/MainApplication.cs")]
    [InlineData("Storyboarder/Folio.Android/Platforms/Android/MainApplication.cs")]
    [InlineData("Foreword/Foreword.Android/Platforms/Android/MainApplication.cs")]
    public void EveryHeadInstallsTheSharedHooks(string file)
    {
        var text = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), file));

        Assert.Contains("MauiCrashHooks.Install(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UnobservedTaskException +=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultUncaughtExceptionHandler =", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHooksWriteEveryRouteToTheUploadedLog()
    {
        var text = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), "Storyworthtelling.Maui/Diagnostics/MauiCrashHooks.cs"));

        foreach (var route in new[] { "DefaultUncaughtExceptionHandler", "AppDomain.CurrentDomain.UnhandledException", "TaskScheduler.UnobservedTaskException" })
        {
            Assert.Contains(route, text, StringComparison.Ordinal);
        }
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(text, @"MauiLog\.Current\?\.Write\(").Count);
    }
}
