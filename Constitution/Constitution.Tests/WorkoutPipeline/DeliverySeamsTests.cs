using Constitution.Domain.Notices;
using Constitution.Domain.Voice;
using Constitution.ReadBack;
using Constitution.WorkoutDelivery;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>P13: the pure halves of the delivery-side seams the head builds for the weekly run.</summary>
public class DeliverySeamsTests
{
    private const string Root = "/data/nobackup";

    [Fact]
    public void ExpectedLivesInTheReadBackFolder()
    {
        Assert.Equal(Path.Combine(Root, "read-back", "expected.json"), ReadBackPaths.Expected(Root));
    }

    [Fact]
    public void EveryReadBackFileSharesOneFolder()
    {
        var folder = ReadBackPaths.Folder(Root);

        foreach (var path in new[] { ReadBackPaths.Expected(Root), ReadBackPaths.Proof(Root), ReadBackPaths.Ledger(Root), ReadBackPaths.Status(Root), ReadBackPaths.Results(Root) })
        {
            Assert.Equal(folder, Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void DeliveredLivesUnderTheRunsOwnFolder()
    {
        Assert.Equal(Path.Combine(Root, "workout-week", "delivered.json"), WorkoutWeekPaths.Delivered(Root));
        Assert.NotEqual(ReadBackPaths.Folder(Root), WorkoutWeekPaths.Folder(Root));
    }

    [Fact]
    public void PathsStayInsideTheRoot()
    {
        Assert.StartsWith(Root, WorkoutWeekPaths.Delivered(Root), StringComparison.Ordinal);
        Assert.StartsWith(Root, ReadBackPaths.Expected(Root), StringComparison.Ordinal);
    }

    [Fact]
    public void ABlankRootIsRefused()
    {
        Assert.ThrowsAny<ArgumentException>(() => ReadBackPaths.Expected(" "));
        Assert.ThrowsAny<ArgumentException>(() => WorkoutWeekPaths.Delivered(""));
    }

    [Fact]
    public void TheNoticeAccessReachesTheOneBoard()
    {
        var access = new ConnectionNoticeBoardAccess();
        var viaAccess = access.With(board => board);
        var viaStatic = Constitution.Settings.ConnectionNoticeBoard.With(board => board);

        Assert.Same(viaStatic, viaAccess);
    }

    [Fact]
    public async Task ThePlainLineIsKeptWhenNotVoiced()
    {
        var seat = new SeatLineEncouragement((request, _) => Task.FromResult(new SeatLine(request.Seat, request.Fallback, IsVoiced: false)));

        var line = await seat.LineAsync(new ProgressionFacts("Squat holding a weight at your chest", 15, 10), Est(11, 3, 6), CancellationToken.None);

        Assert.Equal("15 lb today, up from 10.", line!.Text);
        Assert.False(line.IsVoiced);
    }

    [Fact]
    public async Task AVoicedLineCarriesItsFlag()
    {
        var seat = new SeatLineEncouragement((request, _) => Task.FromResult(new SeatLine(request.Seat, "well done", IsVoiced: true)));

        var line = await seat.LineAsync(new ProgressionFacts("Squat holding a weight at your chest", 15, 10), Est(11, 3, 6), CancellationToken.None);

        Assert.Equal("well done", line!.Text);
        Assert.True(line.IsVoiced);
    }

    [Fact]
    public async Task NoNewsAsksNobody()
    {
        var asked = false;
        var seat = new SeatLineEncouragement((request, _) =>
        {
            asked = true;
            return Task.FromResult(new SeatLine(request.Seat, request.Fallback, IsVoiced: false));
        });

        var line = await seat.LineAsync(new ProgressionFacts("Squat holding a weight at your chest", 10, 10), Est(11, 3, 6), CancellationToken.None);

        Assert.Null(line);
        Assert.False(asked);
    }
}
