using System.Text;
using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

/// <summary>
/// A library scan whose Drive reads all start failing used to log one warning, with a stack, per file
/// for the rest of its time budget — thousands of lines, enough to roll the log over. The tally keeps
/// the first few, sums the rest, and says when to stop asking Drive.
/// </summary>
public class ScanFailureTallyTests
{
    [Fact]
    public void TwoThousandFailuresLogThreeDetailLinesAndOneSummary()
    {
        var tally = new ScanFailureTally(total: 2000);
        var lines = new List<string>();

        for (var file = 1; file <= 2000; file++)
        {
            // Alternating kinds with a success between, so the breaker stays out of it.
            var line = tally.Failed(file, file % 2 == 0 ? 401 : null, "IOException", attempts: 2);
            if (line is not null)
            {
                lines.Add(line);
            }

            tally.Succeeded();
        }

        Assert.Equal(3, lines.Count);
        Assert.Contains("file 1 of 2000", lines[0]);
        Assert.Contains("file 3 of 2000", lines[2]);
        Assert.Contains("HTTP 401", lines[1]);
        Assert.Contains("2 attempts", lines[0]);

        var summary = tally.Summary();
        Assert.NotNull(summary);
        Assert.Contains("read failures 2000 of 4000", summary);
        Assert.Contains("HTTP 401×1000", summary);
        Assert.Contains("IOException×1000", summary);
    }

    [Fact]
    public void ARunWithNoFailuresHasNoSummary()
    {
        var tally = new ScanFailureTally(total: 3);
        tally.Succeeded();
        tally.EndBatch();

        Assert.Null(tally.Summary());
        Assert.False(tally.Tripped);
    }

    [Fact]
    public void AMixedRunDoesNotTrip()
    {
        var tally = new ScanFailureTally(total: 800);
        var file = 0;
        for (var batch = 0; batch < 100; batch++)
        {
            for (var i = 0; i < 8; i++)
            {
                file++;
                if (i % 3 == 0)
                {
                    tally.Failed(file, 403, "DriveReadException", attempts: 2);
                }
                else
                {
                    tally.Succeeded();
                }
            }

            tally.EndBatch();
            Assert.False(tally.Tripped);
        }
    }

    [Fact]
    public void AWholeBatchFailingWithOneStatusTrips()
    {
        var tally = new ScanFailureTally(total: 80);
        for (var file = 1; file <= 8; file++)
        {
            tally.Failed(file, 401, "DriveReadException", attempts: 2);
        }

        Assert.False(tally.Tripped);
        tally.EndBatch();

        Assert.True(tally.Tripped);
        Assert.Contains("stopped", tally.Summary());
    }

    [Fact]
    public void AWholeBatchFailingWithDifferentStatusesDoesNotTrip()
    {
        var tally = new ScanFailureTally(total: 80);
        for (var file = 1; file <= 8; file++)
        {
            tally.Failed(file, file % 2 == 0 ? 401 : 500, "DriveReadException", attempts: 2);
        }

        tally.EndBatch();

        Assert.False(tally.Tripped);
    }

    [Fact]
    public void SixteenStraightFailuresTripWhateverTheirKind()
    {
        var tally = new ScanFailureTally(total: 80);
        for (var file = 1; file <= 15; file++)
        {
            tally.Failed(file, file % 2 == 0 ? 401 : null, "IOException", attempts: 2);
        }

        Assert.False(tally.Tripped);
        tally.Failed(16, null, "IOException", attempts: 2);

        Assert.True(tally.Tripped);
    }

    [Fact]
    public void ASuccessResetsTheStraightCount()
    {
        var tally = new ScanFailureTally(total: 80);
        for (var file = 1; file <= 30; file++)
        {
            if (file == 15)
            {
                tally.Succeeded();
                continue;
            }

            tally.Failed(file, null, "IOException", attempts: 2);
        }

        Assert.False(tally.Tripped);
    }

    [Fact]
    public void AFiveThousandFailureScanWritesWellUnderEightKilobytesOfLog()
    {
        // The scanner's loop, simulated: batches of eight, every read failing, logging each line the
        // tally hands back, stopping when it trips — and, to show the logging is bounded on its own,
        // a second pass that ignores the breaker entirely.
        foreach (var obeyBreaker in new[] { true, false })
        {
            var log = new StringBuilder();
            var tally = new ScanFailureTally(total: 5000);
            for (var start = 1; start <= 5000; start += 8)
            {
                for (var file = start; file < start + 8 && file <= 5000; file++)
                {
                    var line = tally.Failed(file, 401, "DriveReadException", attempts: 2);
                    if (line is not null)
                    {
                        log.AppendLine(line);
                    }
                }

                tally.EndBatch();
                if (obeyBreaker && tally.Tripped)
                {
                    break;
                }
            }

            log.AppendLine(tally.Summary());

            Assert.True(Encoding.UTF8.GetByteCount(log.ToString()) < 1024, log.ToString());
        }
    }
}
