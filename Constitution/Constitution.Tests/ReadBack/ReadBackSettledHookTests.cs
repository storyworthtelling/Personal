using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.ReadBack;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.ReadBack;

/// <summary>The learning is told of a settled day inside the step that keeps it, before the page can go.</summary>
public class ReadBackSettledHookTests
{
    private sealed class Hook(CallLog calls) : IReadBackSettled
    {
        public List<(ExpectedDocument Document, DocumentResult Result)> Told { get; } = [];

        public Exception? Fails { get; set; }

        public void Settled(ExpectedDocument document, DocumentResult result)
        {
            calls.Add("Learn");
            Told.Add((document, result));
            if (Fails is not null)
            {
                throw Fails;
            }
        }
    }

    private static (Rig Rig, Hook Hook, ReadBackService Service) Arranged()
    {
        var rig = new Rig(false, Workout());
        rig.Vision.Reply = document => Read(document, PenStrokes.Seen, (2, "done"));
        var hook = new Hook(rig.Calls);
        var service = new ReadBackService(rig.Expected, rig.Tablet, rig.Vision, rig.Results, rig.Ledger, rig.Proof, rig.Notices, rig.Log, () => rig.Now, hook);
        return (rig, hook, service);
    }

    [Fact]
    public async Task TheDayIsLearnedFromBeforeItIsKept()
    {
        var (rig, hook, service) = Arranged();

        var report = await service.Run();

        Assert.IsType<DocumentReadBack.Settled>(Assert.Single(report.Documents));
        var told = Assert.Single(hook.Told);
        Assert.Equal(Workout().Name, told.Document.Name);
        Assert.Equal(Workout().Name, told.Result.Name);
        var calls = rig.Calls.Calls;
        Assert.True(calls.IndexOf("SaveResult") < calls.IndexOf("Learn"));
        Assert.True(calls.IndexOf("Learn") < calls.IndexOf("MarkSaved"));
        Assert.True(calls.IndexOf("MarkSaved") < calls.IndexOf("Trash"));
    }

    [Fact]
    public async Task ALearningFailureKeepsThePage()
    {
        var (rig, hook, service) = Arranged();
        hook.Fails = new InvalidOperationException("The record could not be read.");

        var report = await service.Run();

        var failed = Assert.IsType<DocumentReadBack.Failed>(Assert.Single(report.Documents));
        Assert.Equal(ReadBackStage.Persist, failed.Stage);
        Assert.DoesNotContain("Trash", rig.Calls.Calls);
        Assert.DoesNotContain("MarkSaved", rig.Calls.Calls);
    }

    [Fact]
    public async Task AnUnreadableLedgerIsNotLearnedFrom()
    {
        var (rig, hook, service) = Arranged();
        rig.Ledger.Unreadable = true;

        await service.Run();

        Assert.Empty(hook.Told);
    }

    [Fact]
    public async Task ADigestTeachesNothing()
    {
        var rig = new Rig(false, Digest());
        var hook = new Hook(rig.Calls);
        rig.Now = Morning.AddDays(30);
        var service = new ReadBackService(rig.Expected, rig.Tablet, rig.Vision, rig.Results, rig.Ledger, rig.Proof, rig.Notices, rig.Log, () => rig.Now, hook);

        await service.Run();

        Assert.Empty(hook.Told);
    }
}
