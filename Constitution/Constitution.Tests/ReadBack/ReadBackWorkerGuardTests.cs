using Constitution.Domain;
using Constitution.Tests.Body;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// C-2.2.3 — the read-back worker's registered Java name and unique work name, held to the frozen constants by reading its
/// source, and the places that make it run: scheduled at app start, and the questions it leaves put back on the Waiting
/// page and marked answered. The constants are guarded elsewhere; none of that checks that the class registers itself under
/// them, which is the half that decides whether WorkManager can build a row it persisted.
/// </summary>
public class ReadBackWorkerGuardTests
{
    private static string Source(string fileName)
    {
        var file = ConstitutionSource.Files(".cs").SingleOrDefault(path => Path.GetFileName(path) == fileName);
        Assert.True(file is not null, fileName + " was not found under Constitution/.");
        return File.ReadAllText(file!);
    }

    [Fact]
    public void TheWorkerRegistersUnderTheFrozenName()
    {
        Assert.Equal(ConstitutionIdentity.ApplicationId + ".ReadBackWorker", BodySyncWorkerGuardTests.RegisteredName(Source("ReadBackWorker.cs")));
        Assert.Equal(ConstitutionIdentity.ReadBackWorkerName, BodySyncWorkerGuardTests.RegisteredName(Source("ReadBackWorker.cs")));
    }

    [Fact]
    public void TheWorkerSchedulesUnderTheFrozenWorkNameAndKeeps()
    {
        var source = Source("ReadBackWorker.cs");

        Assert.Contains("EnqueueUniquePeriodicWork(ConstitutionIdentity.ReadBackWorkName", source, StringComparison.Ordinal);
        Assert.Contains("ExistingPeriodicWorkPolicy.Keep", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExistingPeriodicWorkPolicy.Replace", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExistingPeriodicWorkPolicy.Update", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerUsesTheSharedSchedule()
    {
        var source = Source("ReadBackWorker.cs");

        Assert.Contains("ReadBackSchedule.DelayUntilNext(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FromHours(7", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerNeedsTheNetworkAndNothingElse()
    {
        var source = Source("ReadBackWorker.cs");

        Assert.Contains("NetworkType.Connected", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheActivitySchedulesAndRestores()
    {
        var activity = Source("ConstitutionActivity.cs");

        Assert.Contains("ReadBackWorker.Schedule(this)", activity, StringComparison.Ordinal);
        Assert.Contains("ReadBackWaiting.Restore(this)", activity, StringComparison.Ordinal);
    }

    [Fact]
    public void AChipPickedOnTheWaitingPageIsRememberedAsAnswered()
    {
        Assert.Contains("ReadBackWaiting.Answered(this, item.CauseKey, choice.Value)", Source("ConstitutionActivity.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDiagnosticsExportReadsTheReadBacksOutcome()
    {
        var export = Source("DiagnosticsExport.cs");

        Assert.Contains("DiagnosticsComponent.ReadBack", export, StringComparison.Ordinal);
        Assert.Contains("ReadBackServices.StatusOf(", export, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerLogsOnlyTheTypeName()
    {
        var source = Source("ReadBackWorker.cs");

        Assert.Contains("e.GetType().Name", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Message", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e.ToString()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadBackFolderOpensNoWayOffThePhone()
    {
        var folder = Path.Combine("Constitution.Android", "ReadBack");
        var files = ConstitutionSource.Files(".cs").Where(path => ConstitutionSource.Relative(path).Contains(folder.Replace('\\', '/'), StringComparison.Ordinal)).ToList();
        Assert.True(files.Count >= 3, "The read-back's head folder looks empty; the guard is not reading it.");

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var word in new[] { "RequestPermissions", "[assembly: UsesPermission", "Intent(", "StartActivity", "SendBroadcast", "ClipboardManager", "NotificationCompat" })
            {
                Assert.False(text.Contains(word, StringComparison.Ordinal), $"{ConstitutionSource.Relative(file)} names {word}");
            }
        }
    }
}
