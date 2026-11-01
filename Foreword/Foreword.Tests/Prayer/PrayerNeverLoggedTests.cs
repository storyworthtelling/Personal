using System.Reflection;
using System.Text.RegularExpressions;
using Foreword.Domain.Prayer;
using Foreword.Infrastructure.Prayer;
using Microsoft.Extensions.Logging;
using Storyworthtelling.Maui.Diagnostics;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// A2 (LOG-LOOP-PLAN.md §4.6): the delivery log now leaves the phone in the weekly upload, so nothing the owner
/// writes on the prayer page may reach it. The file logger keeps only the template, the level, the exception's
/// type and its stack for the prayer categories (<see cref="PrayerLogCategories.Quiet"/>). These tests hold the
/// three things that rule depends on: every prayer reader is under a quiet category; a sentinel put into a log
/// call from one — as an argument, as an exception message, as a whole message — never reaches the file; and the
/// prayer code never builds a log message by interpolation, which is the one route a template could carry words.
/// </summary>
public sealed partial class PrayerNeverLoggedTests : IDisposable
{
    private const string Sentinel = "a sentinel sentence from the prayer page";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "swt-prayer-log-" + Guid.NewGuid().ToString("N"));

    public PrayerNeverLoggedTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // The same options the head installs (MauiProgram reads PrayerLogCategories.Quiet; see the source test below).
    private FileLogger Logger() =>
        new(new FileLoggerOptions(directory, "delivery.log", 1_000_000, _ => true, PrayerLogCategories.Quiet));

    public static TheoryData<string> PrayerCategories()
    {
        var data = new TheoryData<string>();
        foreach (var type in PrayerTypes())
        {
            data.Add(type.FullName!);
        }
        return data;
    }

    private static IEnumerable<Type> PrayerTypes() =>
        new[] { typeof(PrayerText).Assembly, typeof(PrayerDeliveryService).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsInterface && (typeof(IPrayerReader).IsAssignableFrom(t) || t == typeof(PrayerDeliveryService)));

    [Fact]
    public void ThereArePrayerTypesToCheck() => Assert.True(PrayerTypes().Count() >= 5, string.Join(", ", PrayerTypes().Select(t => t.Name)));

    [Theory]
    [MemberData(nameof(PrayerCategories))]
    public void EveryPrayerTypeIsUnderAQuietCategory(string category) =>
        Assert.Contains(PrayerLogCategories.Quiet, prefix => category == prefix || category.StartsWith(prefix + ".", StringComparison.Ordinal));

    [Fact]
    public void ThePhonesOwnRotationEditorIsQuietToo() =>
        Assert.Contains("Foreword.Android.Pages.RotationEditPage", PrayerLogCategories.Quiet);

    [Theory]
    [MemberData(nameof(PrayerCategories))]
    public void NothingWrittenOnThePageReachesTheLog(string category)
    {
        var logger = Logger();
        var sink = logger.CreateLogger(category);
        Exception thrown;
        try
        {
            throw new InvalidOperationException("the reply quoted: " + Sentinel, new FormatException(Sentinel));
        }
        catch (InvalidOperationException e)
        {
            thrown = e;
        }

        sink.LogWarning(thrown, "Transcription failed for {Date}", Sentinel);
        sink.LogInformation("Read {Text}", PrayerText.Of(Sentinel));
        sink.LogInformation(Sentinel);
        sink.LogError(thrown, Sentinel);

        var text = logger.ReadAll();
        Assert.DoesNotContain(Sentinel, text, StringComparison.Ordinal);
        Assert.Contains("Transcription failed for {Date}", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException ← FormatException", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePrayerCodeNeverBuildsALogMessageByInterpolation()
    {
        var root = Path.Combine(ForewordRoot(), "Foreword.Infrastructure");
        var files = Directory.EnumerateFiles(Path.Combine(root, "Prayer"), "*.cs")
            .Append(Path.Combine(root, "Pdf", "PrayerPages.cs"))
            .Append(Path.Combine(ForewordRoot(), "Foreword.Android", "Pages", "RotationEditPage.xaml.cs"));

        var interpolated = files
            .SelectMany(f => LogCall().Matches(File.ReadAllText(f)).Where(m => m.Value.Contains("$\"", StringComparison.Ordinal) || m.Value.Contains("$@\"", StringComparison.Ordinal)).Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .ToList();

        Assert.True(interpolated.Count == 0, "Log messages built by interpolation in prayer code:\n" + string.Join("\n", interpolated));
    }

    [Fact]
    public void TheHeadInstallsTheQuietCategories()
    {
        var program = File.ReadAllText(Path.Combine(ForewordRoot(), "Foreword.Android", "MauiProgram.cs"));

        Assert.Contains("PrayerLogCategories.Quiet", program, StringComparison.Ordinal);
        Assert.Contains("MauiLog.Install(", program, StringComparison.Ordinal);
        Assert.Contains("\"delivery.log\"", program, StringComparison.Ordinal);
    }

    private static string ForewordRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Foreword.Infrastructure")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Foreword's project folder was not found above the test output.");
    }

    [GeneratedRegex(@"\.Log(?:Trace|Debug|Information|Warning|Error|Critical)\([^;]*;", RegexOptions.Singleline)]
    private static partial Regex LogCall();
}
