using Microsoft.Extensions.Logging;
using Storyworthtelling.Diagnostics;
using Storyworthtelling.Maui.Diagnostics;

namespace Storyworthtelling.Maui.Tests.Diagnostics;

// The one MAUI file logger (LOG-LOOP-PLAN.md §4.4): Index's refresh.log, Folio's pipeline.log and Foreword's
// delivery.log, each under its own name and cap. A full file is sealed into the log loop's outbox rather than
// thrown away, Foreword gets the cap it never had, and two category rules carry what the heads need: Folio's
// error bodies are cut at 2 KB, and Foreword's prayer categories keep only a template, a level, the exception
// type and its stack.
public sealed class FileLoggerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "swt-filelogger-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset At = new(2026, 11, 1, 9, 30, 15, TimeSpan.Zero);

    public FileLoggerTests() => Directory.CreateDirectory(directory);

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

    private FileLogger Logger(long maxBytes = 1_000_000, string own = "Index", IReadOnlyList<string>? quiet = null, int? maxMessageChars = null) =>
        new(new FileLoggerOptions(directory, "refresh.log", maxBytes, category => category.StartsWith(own, StringComparison.Ordinal), quiet, maxMessageChars), () => At);

    [Fact]
    public void ALineCarriesTimeLevelShortCategoryAndMessage()
    {
        var logger = Logger();

        logger.CreateLogger("Index.Infrastructure.Refresh").LogWarning("Refresh failed for {Count} accounts", 2);

        Assert.Equal("[2026-11-01 09:30:15] [WARN] Refresh: Refresh failed for 2 accounts\n", File.ReadAllText(logger.LivePath));
        Assert.EndsWith("refresh.log", logger.LivePath, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnCategoriesLogInformationAndOthersOnlyWarnings()
    {
        var logger = Logger();

        logger.CreateLogger("Index.Refresh").LogInformation("own info");
        logger.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").LogInformation("framework chatter");
        logger.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").LogWarning("framework warning");

        var text = File.ReadAllText(logger.LivePath);
        Assert.Contains("own info", text, StringComparison.Ordinal);
        Assert.DoesNotContain("framework chatter", text, StringComparison.Ordinal);
        Assert.Contains("framework warning", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExceptionCarriesItsWholeChainAndStack()
    {
        var logger = Logger();
        Exception thrown;
        try
        {
            throw new InvalidOperationException("outer words", new IOException("the real cause"));
        }
        catch (InvalidOperationException e)
        {
            thrown = e;
        }

        logger.CreateLogger("Index.Refresh").LogError(thrown, "Refresh threw");

        var text = File.ReadAllText(logger.LivePath);
        Assert.Contains("InvalidOperationException: outer words", text, StringComparison.Ordinal);
        Assert.Contains("IOException: the real cause", text, StringComparison.Ordinal);
        Assert.Contains(nameof(AnExceptionCarriesItsWholeChainAndStack), text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFullFileIsSealedIntoTheOutboxNotThrownAway()
    {
        var logger = Logger(maxBytes: 200);
        var sink = logger.CreateLogger("Index.Refresh");

        for (var i = 0; i < 10; i++)
        {
            sink.LogInformation("line number {Number} with some words to fill it", i);
        }

        var sealedSegments = logger.Outbox.SegmentsFor("refresh.log");
        Assert.NotEmpty(sealedSegments);
        Assert.All(sealedSegments, s => Assert.True(s.Bytes <= 260, $"a sealed segment of {s.Bytes} bytes"));
        var all = logger.ReadAll();
        for (var i = 0; i < 10; i++)
        {
            Assert.Contains($"line number {i} ", all, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnOldBackupFromTheDiscardingRollIsSealedOnFirstUse()
    {
        File.WriteAllText(Path.Combine(directory, "refresh.log.1"), "[old] the older half\n");

        var logger = Logger();
        logger.CreateLogger("Index.Refresh").LogInformation("new line");

        Assert.False(File.Exists(Path.Combine(directory, "refresh.log.1")));
        Assert.Contains("the older half", logger.ReadAll(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClearEmptiesTheLiveFileAndThisLogsSegmentsOnly()
    {
        var logger = Logger(maxBytes: 120);
        var other = new LogOutbox(directory);
        File.WriteAllText(Path.Combine(directory, "pipeline.log"), "another log\n");
        other.Seal(Path.Combine(directory, "pipeline.log"));
        for (var i = 0; i < 5; i++)
        {
            logger.CreateLogger("Index.Refresh").LogInformation("filler line {Number} for the cap", i);
        }

        logger.Clear();

        Assert.False(logger.HasEntries);
        Assert.Empty(logger.Outbox.SegmentsFor("refresh.log"));
        Assert.Single(logger.Outbox.SegmentsFor("pipeline.log"));
    }

    [Fact]
    public void AMessageCanBeCutAtACap()
    {
        var logger = Logger(maxMessageChars: 2048);

        logger.CreateLogger("Index.Refresh").LogWarning("Body: {Body}", new string('x', 5000));

        var line = File.ReadAllText(logger.LivePath);
        Assert.DoesNotContain(new string('x', 2049), line, StringComparison.Ordinal);
        Assert.Contains("[cut: 5006 characters]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuietCategoryKeepsOnlyTheTemplateTheLevelAndTheExceptionsTypeAndStack()
    {
        const string sentinel = "a sentinel line of private writing";
        var logger = Logger(own: "Foreword", quiet: ["Foreword.Infrastructure.Prayer"]);
        Exception thrown;
        try
        {
            throw new FormatException("could not read: " + sentinel);
        }
        catch (FormatException e)
        {
            thrown = e;
        }

        var quiet = logger.CreateLogger("Foreword.Infrastructure.Prayer.PrayerDeliveryService");
        quiet.LogWarning(thrown, "Transcribed {Text}", sentinel);
        quiet.LogInformation(sentinel);
        logger.CreateLogger("Foreword.Infrastructure.PrayerPagesNotUnderIt").LogInformation("not quiet {Text}", "visible");

        var text = File.ReadAllText(logger.LivePath);
        Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
        Assert.Contains("Transcribed {Text}", text, StringComparison.Ordinal);
        Assert.Contains("FormatException", text, StringComparison.Ordinal);
        Assert.Contains("(no template · #", text, StringComparison.Ordinal);
        Assert.Contains("not quiet visible", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnhandledExceptionIsWrittenThroughTheSameLog()
    {
        var logger = Logger();

        logger.Write("Unhandled", LogLevel.Critical, "The app is going down", new InvalidOperationException("boom"));

        var text = File.ReadAllText(logger.LivePath);
        Assert.Contains("[CRIT] Unhandled: The app is going down", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: boom", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALoggingFailureNeverReachesTheCaller()
    {
        // A "directory" that is really a file: every write to it fails.
        var blocker = Path.Combine(directory, "blocker");
        File.WriteAllText(blocker, "a file, not a directory");
        var logger = new FileLogger(new FileLoggerOptions(Path.Combine(blocker, "logs"), "refresh.log", 1000, _ => true));

        var write = Record.Exception(() => logger.CreateLogger("Index.Refresh").LogWarning("still fine"));

        Assert.Null(write);
    }
}
