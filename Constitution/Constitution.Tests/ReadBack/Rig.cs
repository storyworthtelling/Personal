using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Tests.ReadBack;

/// <summary>The service with every seam faked, the tablet holding what was delivered, and the morning's clock in the owner's own time.</summary>
internal sealed class Rig
{
    public Rig(bool proven = false, params ExpectedDocument[] documents)
    {
        Expected = new FakeExpected(Calls, documents);
        Tablet = new FakeTablet(Calls);
        Vision = new FakeVision(Calls);
        Results = new FakeResults(Calls);
        Ledger = new FakeLedger(Calls);
        Proof = new FakeProof(Calls, proven);
        Notices = new FakeBoard(Calls);
        Log = new FakeLog();
        foreach (var document in documents)
        {
            Tablet.Has(document);
        }

        Service = new ReadBackService(Expected, Tablet, Vision, Results, Ledger, Proof, Notices, Log, () => Now);
    }

    public CallLog Calls { get; } = new();

    public FakeExpected Expected { get; }

    public FakeTablet Tablet { get; }

    public FakeVision Vision { get; }

    public FakeResults Results { get; }

    public FakeLedger Ledger { get; }

    public FakeProof Proof { get; }

    public FakeBoard Notices { get; }

    public FakeLog Log { get; }

    public DateTimeOffset Now { get; set; } = ReadBackFixture.Morning;

    public ReadBackService Service { get; }

    public async Task<DocumentReadBack> RunOne()
    {
        var report = await Service.Run();
        return Assert.Single(report.Documents);
    }
}
