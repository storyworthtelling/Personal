using Constitution.Domain;

namespace Constitution.Tests.DigestPages;

/// <summary>
/// Job 4 — a built thing nothing runs is the failure P10's BookPage was. These read the head by content: the Saturday digest has a worker with a frozen
/// registration, the worker calls the run through the shared gate, the activity schedules it and opens it, and the run is the one that delivers.
/// </summary>
public class DigestReachabilityTests
{
    private static string Head(string relative) => File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "Constitution.Android", relative));

    [Fact]
    public void TheWorkerIsRegisteredByTheFrozenNames()
    {
        var source = Head("SaturdayDigest/DigestWorker.cs");
        Assert.Contains("[Register(ConstitutionIdentity.DigestWorkerName)]", source, StringComparison.Ordinal);
        Assert.Contains(": Worker", source, StringComparison.Ordinal);
        Assert.Contains("EnqueueUniquePeriodicWork(ConstitutionIdentity.DigestWorkName", source, StringComparison.Ordinal);
        Assert.Contains("ExistingPeriodicWorkPolicy.Keep", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNamesAreLiteralsNotNameof()
    {
        var identity = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "Constitution.Domain", "ConstitutionIdentity.cs"));
        Assert.Matches(@"DigestWorkName = ""constitution-saturday-digest"";", identity);
        Assert.Matches(@"DigestWorkerName = ApplicationId \+ "".DigestWorker"";", identity);
        Assert.DoesNotMatch(@"Digest\w*Name\s*=\s*nameof", identity);
    }

    [Fact]
    public void TheWorkerRunsTheDigestThroughTheSharedGate()
    {
        var worker = Head("SaturdayDigest/DigestWorker.cs");
        Assert.Contains("DigestServices.TryRunAsync(", worker, StringComparison.Ordinal);
        var services = Head("SaturdayDigest/DigestServices.cs");
        Assert.Contains("RunGate.Shared.RunAsync(", services, StringComparison.Ordinal);
        Assert.Contains("SaturdayDigestRun", services, StringComparison.Ordinal);
        Assert.Contains("LiveDigestSources", services, StringComparison.Ordinal);
        Assert.Contains("ConstitutionDocumentRenderer", Source("Constitution.Infrastructure/Documents/SaturdayDigestRun.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheActivitySchedulesItAndRunsItOnOpen()
    {
        var activity = Head("ConstitutionActivity.cs");
        Assert.Contains("DigestWorker.Schedule(this)", activity, StringComparison.Ordinal);
        Assert.Contains("DigestWorker.RunOnOpen(this)", activity, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerAttachesTheNoticePoster()
    {
        Assert.Contains("NoticePoster.Attach(", Head("SaturdayDigest/DigestWorker.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlannerNotesAreReadAndReachThePlanner()
    {
        // Owner decision amending C-2.2.3: the read-back reads them, and the meal run's seams carry them to the planner.
        Assert.Contains("DigestServices.PlannerNotesOf(", Head("ReadBack/ReadBackServices.cs"), StringComparison.Ordinal);
        Assert.Contains("PlannerNotes: ", Head("MealWeek/MealWeekServices.cs"), StringComparison.Ordinal);
        Assert.Contains("ExpectedDocument.ForDigest(", Source("Constitution.Infrastructure/Documents/SaturdayDigestRun.cs"), StringComparison.Ordinal);
    }

    private static string Source(string relative) => File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative));
}
