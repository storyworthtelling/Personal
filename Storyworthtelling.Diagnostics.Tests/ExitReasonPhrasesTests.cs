namespace Storyworthtelling.Diagnostics.Tests;

// G6: a process killed by Android, an ANR or a force-stop leaves nothing unless the next launch asks
// ActivityManager why the last one ended. The codes are ApplicationExitInfo.REASON_*, read from the
// Mono.Android reference assembly (Android.App.ApplicationExitInfoReason) rather than from memory.
public class ExitReasonPhrasesTests
{
    [Theory]
    [InlineData(0, "UNKNOWN")]
    [InlineData(1, "EXIT_SELF")]
    [InlineData(2, "SIGNALED")]
    [InlineData(3, "LOW_MEMORY")]
    [InlineData(4, "CRASH")]
    [InlineData(5, "CRASH_NATIVE")]
    [InlineData(6, "ANR")]
    [InlineData(7, "INITIALIZATION_FAILURE")]
    [InlineData(8, "PERMISSION_CHANGE")]
    [InlineData(9, "EXCESSIVE_RESOURCE_USAGE")]
    [InlineData(10, "USER_REQUESTED")]
    [InlineData(11, "USER_STOPPED")]
    [InlineData(12, "DEPENDENCY_DIED")]
    [InlineData(13, "OTHER")]
    [InlineData(14, "FREEZER")]
    [InlineData(15, "PACKAGE_STATE_CHANGE")]
    [InlineData(16, "PACKAGE_UPDATED")]
    public void EveryPlatformCodeHasALabel(int reason, string label)
    {
        Assert.Equal(label, ExitReasonPhrases.Label(reason));
    }

    [Fact]
    public void AnUnseenCodeIsNamedByNumberRatherThanGuessed()
    {
        Assert.Equal("REASON_99", ExitReasonPhrases.Label(99));
    }

    [Fact]
    public void AForceStopReadsAsWhatItDoesToAnSmsApp()
    {
        var sentence = ExitReasonPhrases.Describe(new ExitRecord(11, 0, null, 1_000, 0));

        Assert.Equal(
            "USER_STOPPED: the app was force-stopped; Android delivers nothing to it until it is opened again.",
            sentence);
    }

    [Fact]
    public void TheSystemsOwnDescriptionAndImportanceRideAlong()
    {
        var sentence = ExitReasonPhrases.Describe(new ExitRecord(6, 0, "Input dispatching timed out", 1_000, 100));

        Assert.Equal(
            "ANR: the app stopped responding and Android ended it (while in the foreground). System says: Input dispatching timed out.",
            sentence);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(9, true)]
    [InlineData(11, true)]
    [InlineData(1, false)]
    [InlineData(10, false)]
    [InlineData(16, false)]
    public void ProblemsAreTheExitsTheOwnerDidNotAskFor(int reason, bool problem)
    {
        Assert.Equal(problem, ExitReasonPhrases.IsProblem(reason));
    }

    [Fact]
    public void OnlyExitsSinceTheLastReadAreNewOldestFirst()
    {
        var records = new[]
        {
            new ExitRecord(4, 0, null, 3_000, 0),
            new ExitRecord(6, 0, null, 1_000, 0),
            new ExitRecord(11, 0, null, 2_000, 0),
        };

        var fresh = ExitReasonPhrases.NewSince(records, lastSeenTimestamp: 1_000);

        Assert.Equal(new long[] { 2_000, 3_000 }, fresh.Select(r => r.Timestamp));
    }
}
