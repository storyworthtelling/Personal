using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// A date detected in a message is proposed, never written silently and never always-asked
/// (D37). Before proposing, Post checks it against what is already in the day — and the
/// owner asked for BOTH kinds of clash: an outright overlap, and a margin too tight to
/// cross town in.
/// </summary>
public class CalendarConflictTests
{
    private static readonly DateOnly Day = new(2026, 9, 12);

    private static Appointment At(int hour, int minute, int minutes) =>
        new("Something", Day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromMinutes(minutes));

    private static ProposedEvent Proposed(int hour, int minute) =>
        new("Recital", Day, new TimeOnly(hour, minute));

    [Fact]
    public void AClearDayHasNothingToSay()
    {
        Assert.Empty(CalendarConflicts.Against(Proposed(19, 30), [At(9, 0, 60)]));
    }

    [Fact]
    public void AnOverlapIsReported()
    {
        var clashes = CalendarConflicts.Against(Proposed(19, 30), [At(19, 0, 90)]);

        Assert.Single(clashes);
        Assert.Equal(ConflictKind.Overlap, clashes[0].Kind);
    }

    // The owner's second ask, and the one a calendar app usually misses: an appointment
    // that ends eight minutes before the next one starts is not a clear day.
    [Fact]
    public void ATightMarginIsReportedToo()
    {
        var clashes = CalendarConflicts.Against(Proposed(19, 30), [At(18, 30, 55)]);

        Assert.Single(clashes);
        Assert.Equal(ConflictKind.TightMargin, clashes[0].Kind);
    }

    [Fact]
    public void AComfortableGapIsNotAClash()
    {
        Assert.Empty(CalendarConflicts.Against(Proposed(19, 30), [At(17, 0, 60)]));
    }

    // A margin is needed on both sides: leaving somewhere is as much of a problem as
    // arriving somewhere. 20:45 is fifteen minutes after a 19:30 event is assumed to end —
    // clear of it, and not clear enough.
    [Fact]
    public void SomethingStartingRightAfterIsAlsoTight()
    {
        var clashes = CalendarConflicts.Against(Proposed(19, 30), [At(20, 45, 60)]);

        Assert.Single(clashes);
        Assert.Equal(ConflictKind.TightMargin, clashes[0].Kind);
    }

    // An all-day event has no time to compare against, so it cannot clash on the clock.
    // Saying it clashes with everything that day would make the check useless.
    [Fact]
    public void AnAllDayProposalDoesNotClashOnTheClock()
    {
        var allDay = new ProposedEvent("Sports day", Day, StartsAt: null);

        Assert.Empty(CalendarConflicts.Against(allDay, [At(10, 0, 60)]));
    }

    [Fact]
    public void SomethingOnAnotherDayIsNotAClash()
    {
        var elsewhere = new Appointment(
            "Other", Day.AddDays(1).ToDateTime(new TimeOnly(19, 30)), TimeSpan.FromMinutes(60));

        Assert.Empty(CalendarConflicts.Against(Proposed(19, 30), [elsewhere]));
    }

    // Every clash is reported, not just the first: a day with two problems in it is worse
    // than a day with one, and the owner is the one deciding.
    [Fact]
    public void EveryClashIsReported()
    {
        var clashes = CalendarConflicts.Against(Proposed(19, 30), [At(19, 0, 90), At(20, 45, 30)]);

        Assert.Equal(2, clashes.Count);
    }
}
