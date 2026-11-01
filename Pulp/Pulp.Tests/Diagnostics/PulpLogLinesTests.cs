using Pulp.Domain.Calendar;
using Pulp.Domain.Diagnostics;
using Pulp.Domain.LockedOpen;
using Xunit;

namespace Pulp.Tests.Diagnostics;

// Pulp's lines for the log loop (LOG-LOOP-PLAN.md §4.5): the hand-off's outcome by service, the locked
// hand-off's answer, and the nightly calendar write's counts. Never what anyone watched — no title, no
// game, no name — because the settings screen promises exactly that.
public class PulpLogLinesTests
{
    [Fact]
    public void AHandOffNamesTheServiceAndTheOutcomeOnly()
    {
        Assert.Equal("HAND-OFF Example Plus: started", PulpLogLines.HandOff("Example Plus", PulpLogLines.Started));
        Assert.Equal("HAND-OFF Example Plus: not installed", PulpLogLines.HandOff("Example Plus", PulpLogLines.NotInstalled));
        Assert.Equal("HAND-OFF Example Plus: refused (ChildCeiling)", PulpLogLines.HandOff("Example Plus", PulpLogLines.Refused("ChildCeiling")));
    }

    [Fact]
    public void TheLockedHandOffSaysWhatFlyleafAnswered()
    {
        Assert.Equal("LOCKED OPEN started by Flyleaf", PulpLogLines.LockedOpen(LockedOpenHandOff.ResultStarted));
        Assert.Equal("LOCKED OPEN refused by Flyleaf (answer 0); opened plainly", PulpLogLines.LockedOpen(LockedOpenHandOff.ResultRefused));
        Assert.Equal("LOCKED OPEN no answer from Flyleaf; opened plainly", PulpLogLines.LockedOpen(null));
        Assert.Equal("LOCKED OPEN could not be asked (SecurityException); opened plainly", PulpLogLines.LockedOpenNotSent("SecurityException"));
    }

    [Fact]
    public void TheCalendarWriteIsCountsOnly()
    {
        var wrote = new CalendarNightlyOutcome("cal-1", new CalendarSyncResult(2, 1, 0, 5));
        var shared = wrote with { ShareFailures = [new CalendarShareFailure("someone@example.com", new InvalidOperationException("403"))] };

        Assert.Equal("CALENDAR 2 created · 1 moved · 0 deleted · 5 left", PulpLogLines.Calendar(wrote));
        Assert.Equal("CALENDAR 2 created · 1 moved · 0 deleted · 5 left · 1 share failed", PulpLogLines.Calendar(shared));
        Assert.DoesNotContain("example.com", PulpLogLines.Calendar(shared));
        Assert.Equal("CALENDAR not written: no calendar yet", PulpLogLines.Calendar(new CalendarNightlyOutcome(null, null)));
        Assert.Equal("CALENDAR not written: this install only reads it", PulpLogLines.Calendar(new CalendarNightlyOutcome("cal-1", null)));
        Assert.Equal("CALENDAR not run: no household or account yet", PulpLogLines.CalendarSkipped);
    }
}
