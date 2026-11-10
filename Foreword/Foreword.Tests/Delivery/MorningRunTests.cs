using System.Net.Http;
using System.Net.Sockets;
using Foreword.Domain.Delivery;
using Foreword.Infrastructure.Delivery;
using Xunit;

namespace Foreword.Tests.Delivery;

/// <summary>
/// The morning as one run, for the worker and the Deliver now button alike (audit 11 §6–7): each document in its own
/// try, each that lands marked delivered for the day, and a retry that sends only what is missing — so a failure in one
/// cannot take the others down, and a run after a partial morning never puts a second brief on the tablet.
/// </summary>
public class MorningRunTests
{
    private static readonly DateOnly Today = new(2026, 6, 2);

    private string? stored;

    private MorningRun Run() => new(() => stored, value => stored = value);

    [Fact]
    public async Task EachDocumentIsSentAndMarked()
    {
        var sent = new List<MorningDocument>();

        var outcome = await Run().Deliver(Today,
        [
            new(MorningDocument.Brief, () => { sent.Add(MorningDocument.Brief); return Task.FromResult<string?>("8 articles + puzzle"); }),
            new(MorningDocument.Day, () => { sent.Add(MorningDocument.Day); return Task.FromResult<string?>(null); }),
        ]);

        Assert.Equal([MorningDocument.Brief, MorningDocument.Day], sent);
        Assert.False(outcome.AnyFailed);
        Assert.True(MorningLedger.Read(stored, Today).Delivered(MorningDocument.Brief));
        Assert.True(MorningLedger.Read(stored, Today).Delivered(MorningDocument.Day));
    }

    [Fact]
    public async Task AFailureLeavesTheOthersAndTheRetrySendsOnlyWhatIsMissing()
    {
        var briefs = 0;
        var dayFails = true;
        MorningStep[] Steps() =>
        [
            new(MorningDocument.Brief, () => { briefs++; return Task.FromResult<string?>(null); }),
            new(MorningDocument.Day, () => dayFails
                ? throw new HttpRequestException("No such host is known", new SocketException((int)SocketError.HostNotFound))
                : Task.FromResult<string?>(null)),
        ];

        var first = await Run().Deliver(Today, Steps());
        Assert.True(first.Failed(MorningDocument.Day));
        Assert.Contains("The Day did not arrive (no network)", first.Summary(), StringComparison.Ordinal);

        dayFails = false;
        var retry = await Run().Deliver(Today, Steps());

        Assert.Equal(1, briefs);
        Assert.False(retry.AnyFailed);
        Assert.Contains("the brief (earlier this morning)", retry.Summary(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStepThatIsNotDueIsSkipped()
    {
        var outcome = await Run().Deliver(Today, [new(MorningDocument.Bonus, null)]);

        Assert.False(outcome.AnyFailed);
        Assert.False(MorningLedger.Read(stored, Today).Delivered(MorningDocument.Bonus));
    }

    [Fact]
    public async Task ATabletRefusalSaysWhyInItsOwnWords()
    {
        var outcome = await Run().Deliver(Today,
        [
            new(MorningDocument.Brief, () => throw new Storyworthtelling.Remarkable.RemarkableRefusedException(
                Storyworthtelling.Remarkable.RemarkableRefusal.TooLarge, 173_000_000)),
        ]);

        Assert.Contains("over the tablet's 100 MB limit", outcome.Summary(), StringComparison.Ordinal);
    }
}
