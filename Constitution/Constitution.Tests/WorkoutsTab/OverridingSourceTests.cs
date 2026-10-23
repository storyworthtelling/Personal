using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Domain.Workouts.Screens;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>What the owner does on the tab reaches the run: every change the tab records is also kept as an override, first.</summary>
public class OverridingSourceTests
{
    private static SwapResult Swap() => SwapChoices.Check(Snapshot(), Wed, Fri).Result!;

    private sealed class Journal
    {
        public List<string> Calls { get; } = [];
    }

    private sealed class InnerSource(Journal journal) : IWorkoutsSource
    {
        public List<WorkoutsChange> Recorded { get; } = [];

        public Exception? Fails { get; set; }

        public WorkoutsRead Read(DateTimeOffset now)
        {
            journal.Calls.Add("read");
            return new WorkoutsRead.NoPlanYet();
        }

        public void Record(WorkoutsChange change)
        {
            journal.Calls.Add("source");
            if (Fails is not null)
            {
                throw Fails;
            }

            Recorded.Add(change);
        }
    }

    private sealed class Kept(Journal journal) : IWorkoutOverrides
    {
        public PlanOverrides Held { get; private set; } = PlanOverrides.None;

        public Exception? Fails { get; set; }

        public PlanOverrides Load() => Held;

        public void Add(PlanOverrides more)
        {
            journal.Calls.Add("overrides");
            if (Fails is not null)
            {
                throw Fails;
            }

            Held = Held.Then(more);
        }
    }

    private static (OverridingWorkoutsSource Source, InnerSource Inner, Kept Overrides, Journal Journal) Make()
    {
        var journal = new Journal();
        var inner = new InnerSource(journal);
        var kept = new Kept(journal);
        return (new OverridingWorkoutsSource(inner, kept), inner, kept, journal);
    }

    [Fact]
    public void ASwapIsKeptThenRecorded()
    {
        var (source, inner, kept, journal) = Make();
        var change = new WorkoutsChange.Swapped(Swap());

        source.Record(change);

        Assert.Equal(["overrides", "source"], journal.Calls);
        Assert.Same(change, Assert.Single(inner.Recorded));
        var move = Assert.Single(Assert.Single(kept.Held.Groups).Moves);
        Assert.Equal(Wed, move.FromDay);
        Assert.Equal(Fri, move.Day);
    }

    [Fact]
    public void ASkipIsKeptByPersonAndDay()
    {
        var (source, inner, kept, _) = Make();
        var skip = new PendingSkip(Sam, Thu, SessionKind.Strength, Snapshot().SessionOn(Thu)!, null, null, Now);

        source.Record(new WorkoutsChange.SkipCommitted(skip, SkipLedger.Empty));

        Assert.True(kept.Held.IsSkipped(Sam, Thu));
        Assert.Single(inner.Recorded);
    }

    [Fact]
    public void AnUnkeptOverrideStopsTheRecord()
    {
        var (source, inner, kept, _) = Make();
        kept.Fails = new IOException("disk full");

        var failure = Assert.Throws<IOException>(() => source.Record(new WorkoutsChange.Swapped(Swap())));

        Assert.Equal("disk full", failure.Message);
        Assert.Empty(inner.Recorded);
    }

    [Fact]
    public void ALaterFailureKeepsTheOverride()
    {
        var (source, inner, kept, _) = Make();
        inner.Fails = new InvalidOperationException("no plan");

        Assert.Throws<InvalidOperationException>(() => source.Record(new WorkoutsChange.Swapped(Swap())));

        Assert.False(kept.Held.IsEmpty);
    }

    [Fact]
    public void ReadingGoesStraightThrough()
    {
        var (source, _, _, journal) = Make();

        Assert.IsType<WorkoutsRead.NoPlanYet>(source.Read(Now));

        Assert.Equal(["read"], journal.Calls);
    }

    [Fact]
    public void ItNeedsBothHalves()
    {
        var (_, inner, kept, _) = Make();

        Assert.Throws<ArgumentNullException>(() => new OverridingWorkoutsSource(null!, kept));
        Assert.Throws<ArgumentNullException>(() => new OverridingWorkoutsSource(inner, null!));
    }
}
