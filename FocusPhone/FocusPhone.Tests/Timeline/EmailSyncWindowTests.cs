using FocusPhone.Domain.Timeline;
using Xunit;

namespace FocusPhone.Tests.Timeline;

public class EmailSyncWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromDays(30);

    [Fact]
    public void Reconcile_AlwaysFetchesTheFullWindow_EvenWithAPriorSync()
    {
        var lastSync = Now - TimeSpan.FromHours(2);
        Assert.Equal(Now - Window, EmailSyncWindow.Since(reconcile: true, lastSync, Now, Window));
    }

    [Fact]
    public void Incremental_ResumesFromTheLastSuccessfulSync()
    {
        var lastSync = Now - TimeSpan.FromHours(2);
        Assert.Equal(lastSync, EmailSyncWindow.Since(reconcile: false, lastSync, Now, Window));
    }

    [Fact]
    public void Incremental_FallsBackToTheFullWindowOnTheFirstRun()
    {
        Assert.Equal(Now - Window, EmailSyncWindow.Since(reconcile: false, lastSync: null, Now, Window));
    }
}
