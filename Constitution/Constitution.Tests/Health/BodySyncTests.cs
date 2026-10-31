using Constitution.Domain.Body;
using Constitution.Domain.Diagnostics;
using Constitution.Tests.Body;

namespace Constitution.Tests.Health;

/// <summary>C-7.1.3 / R5 — the worker reads overnight where Android allows; otherwise the app reads when it opens and says so.</summary>
public class BodySyncTests
{
    private sealed record Rig(BodySync Sync, FakeHealthSource Source, MemoryBodyStore Store, CapturingSyncStatus Status, CapturingBodyLog Log);

    private static Rig Build(bool background)
    {
        var store = new MemoryBodyStore();
        var source = new FakeHealthSource().Grant(BodySignal.Weight, Samples.Of(Samples.Today, 180));
        source.BackgroundReadsGranted = background;
        var log = new CapturingBodyLog();
        var status = new CapturingSyncStatus();
        var reader = new SignalReader(store, source, () => Samples.Today, log, null);
        return new Rig(new BodySync(reader, source, status, log), source, store, status, log);
    }

    [Fact]
    public async Task FallsBackToOpenTimeReads()
    {
        var rig = Build(background: false);

        // Overnight: the worker wakes, finds it may not read in the background, and reads nothing.
        var worker = await rig.Sync.RunInBackgroundAsync(default);
        Assert.False(worker.Ran);
        Assert.Equal(BodySyncGap.BackgroundNotGranted, worker.Gap);
        Assert.Empty(rig.Source.Asked);
        Assert.True(rig.Store.IsEmpty);

        // The morning: the app opens, and the values are refreshed then; the gap is recorded.
        var opened = await rig.Sync.OpenTimeReadsAsync(default);
        Assert.True(opened.Ran);
        Assert.Equal(BodySyncTrigger.AppOpen, opened.Trigger);
        Assert.Equal(BodySyncGap.BackgroundNotGranted, opened.Gap);
        Assert.Equal(180, Assert.Single(rig.Store.All()).Value);
        var recorded = Assert.Single(rig.Status.Recorded);
        Assert.Equal(BodySyncGap.BackgroundNotGranted, recorded.Gap);
        Assert.Equal(DiagnosticsOutcome.Partial, recorded.Outcome);
    }

    [Fact]
    public async Task TheWorkerReadsWhenBackgroundIsGranted()
    {
        var rig = Build(background: true);

        var worker = await rig.Sync.RunInBackgroundAsync(default);

        Assert.True(worker.Ran);
        Assert.Equal(BodySyncTrigger.Worker, worker.Trigger);
        Assert.Equal(BodySyncGap.None, worker.Gap);
        Assert.Equal(DiagnosticsOutcome.Succeeded, worker.Outcome);
        Assert.Single(rig.Store.All());
        Assert.Single(rig.Status.Recorded);
    }

    [Fact]
    public async Task OpeningWithBackgroundGrantedLeavesNoGap()
    {
        var rig = Build(background: true);

        var opened = await rig.Sync.OpenTimeReadsAsync(default);

        Assert.Equal(BodySyncGap.None, opened.Gap);
        Assert.Equal(DiagnosticsOutcome.Succeeded, opened.Outcome);
    }

    [Fact]
    public async Task ASkippedWorkerDoesNotOverwriteTheLastOpenRead()
    {
        var rig = Build(background: false);
        await rig.Sync.OpenTimeReadsAsync(default);

        await rig.Sync.RunInBackgroundAsync(default);

        Assert.Single(rig.Status.Recorded);
    }

    [Fact]
    public async Task NothingGrantedYetIsNotASuccess()
    {
        var rig = Build(background: false);
        rig.Source.Granted.Clear();

        var opened = await rig.Sync.OpenTimeReadsAsync(default);

        Assert.Equal(DiagnosticsOutcome.NeverRan, opened.Outcome);
    }

    [Theory]
    [InlineData(HealthReadOutcome.Denied, DiagnosticsOutcome.Refused)]
    [InlineData(HealthReadOutcome.Unsupported, DiagnosticsOutcome.Refused)]
    [InlineData(HealthReadOutcome.Failed, DiagnosticsOutcome.Unreachable)]
    public async Task AllSignalsFailingNamesTheShape(HealthReadOutcome how, DiagnosticsOutcome expected)
    {
        var rig = Build(background: true);
        rig.Source.Answer(BodySignal.Weight, new HealthRead(how, []));

        var worker = await rig.Sync.RunInBackgroundAsync(default);

        Assert.Equal(expected, worker.Outcome);
    }

    [Fact]
    public async Task SomeSignalsFailingIsPartial()
    {
        var rig = Build(background: true);
        rig.Source.Answer(BodySignal.Steps, new HealthRead(HealthReadOutcome.Failed, []));

        var worker = await rig.Sync.RunInBackgroundAsync(default);

        Assert.Equal(DiagnosticsOutcome.Partial, worker.Outcome);
    }

    [Fact]
    public async Task TheGapIsLoggedAsAPhraseNotAValue()
    {
        var rig = Build(background: false);

        await rig.Sync.OpenTimeReadsAsync(default);

        var text = string.Join("\n", rig.Log.Lines);
        Assert.Contains("background", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("180", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGapRendersInDiagnosticsAsAnOrdinaryOutcome()
    {
        var outcome = new ComponentOutcome(DiagnosticsComponent.BodySync, DiagnosticsOutcome.Partial);

        Assert.Equal(DiagnosticsComponent.BodySync, outcome.Component);
    }
}
