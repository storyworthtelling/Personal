using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Screens;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>
/// S2: a skip is handed over as the skip and nothing else. It used to carry a whole ledger built from the snapshot the tab loaded, so a
/// source that saved it wrote over everything the morning's read-back had settled since the tab was opened. Now the settling is done at the
/// moment of recording, against the ledger as it stands then.
/// </summary>
public sealed class SkipSettlingTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-skips-");

    public void Dispose() => folder.Delete(recursive: true);

    private string LedgerPath() => Path.Combine(folder.FullName, "ledger.json");

    private static PendingSkip Skip() => new(Sam, Thu, SessionKind.Strength, ThuStrength(), null, null, Est(11, 3, 12));

    private sealed class Journal
    {
        public List<string> Calls { get; } = [];
    }

    private sealed class Inner(Journal journal) : IWorkoutsSource
    {
        public Exception? Fails { get; set; }

        public WorkoutsRead Read(DateTimeOffset now) => new WorkoutsRead.NoPlanYet();

        public void Record(WorkoutsChange change)
        {
            journal.Calls.Add("inner");
            if (Fails is not null)
            {
                throw Fails;
            }
        }
    }

    private sealed class Settler(Journal journal) : ISkipSettler
    {
        public List<SkipCommit> Settled { get; } = [];

        public Exception? Fails { get; set; }

        public void Settle(SkipCommit commit)
        {
            journal.Calls.Add("settle");
            if (Fails is not null)
            {
                throw Fails;
            }

            Settled.Add(commit);
        }
    }

    // ---- what is handed over ----

    [Fact]
    public void ASkipChangeCarriesTheSkipAndNoLedger()
    {
        var members = typeof(WorkoutsChange.SkipCommitted).GetProperties().Select(property => property.PropertyType).ToList();

        Assert.DoesNotContain(typeof(SkipLedger), members);
        Assert.Contains(typeof(PendingSkip), members);
    }

    // ---- the source that settles ----

    [Fact]
    public void ASkipIsSettledThenRecorded()
    {
        var journal = new Journal();
        var settler = new Settler(journal);
        var source = new SettlingWorkoutsSource(new Inner(journal), settler);

        source.Record(new WorkoutsChange.SkipCommitted(Skip()));

        Assert.Equal(["settle", "inner"], journal.Calls);
        Assert.Equal(Thu, Assert.Single(settler.Settled).Skip.Day);
    }

    [Fact]
    public void ASkipThatCannotBeSettledIsNotRecorded()
    {
        var journal = new Journal();
        var source = new SettlingWorkoutsSource(new Inner(journal), new Settler(journal) { Fails = new IOException("disk") });

        Assert.Throws<IOException>(() => source.Record(new WorkoutsChange.SkipCommitted(Skip())));

        Assert.Equal(["settle"], journal.Calls);
    }

    [Fact]
    public void ASwapIsNotSettledAtAll()
    {
        var journal = new Journal();
        var source = new SettlingWorkoutsSource(new Inner(journal), new Settler(journal));

        source.Record(new WorkoutsChange.Swapped(SwapChoices.Check(Snapshot(), Wed, Fri).Result!));

        Assert.Equal(["inner"], journal.Calls);
    }

    [Fact]
    public void TheReadIsPassedStraightThrough()
    {
        var journal = new Journal();
        var source = new SettlingWorkoutsSource(new Inner(journal), new Settler(journal));

        Assert.IsType<WorkoutsRead.NoPlanYet>(source.Read(Est(11, 3, 7)));
    }

    // ---- settling against the ledger as it now stands ----

    [Fact]
    public void TheSkipGoesIntoTheLedgerAsItNowStands()
    {
        var store = new FileLedgerStore(LedgerPath());

        // The tab was opened, and then the morning's read-back settled Tuesday as done.
        store.Save(SkipLedger.Empty.Settle(
            new SessionMarks(Sam, Tue, SessionKind.Strength, ["done"]),
            [new Constitution.Domain.Ink.InkResolution.Ticked("done")]));

        new LedgerSkipSettler(store).Settle(new SkipCommit(Skip()));

        var ledger = store.Load().Ledger;
        Assert.Equal(SessionOutcome.Done, ledger.OutcomeOf(Sam, Tue));
        Assert.Equal(SessionOutcome.Skipped, ledger.OutcomeOf(Sam, Thu));
    }

    [Fact]
    public void SettlingTwiceLeavesOneRecord()
    {
        var store = new FileLedgerStore(LedgerPath());
        var settler = new LedgerSkipSettler(store);

        settler.Settle(new SkipCommit(Skip()));
        settler.Settle(new SkipCommit(Skip()));

        Assert.Single(store.Load().Ledger.Records);
    }

    [Fact]
    public void ALedgerThatCannotBeReadIsNeverWrittenOver()
    {
        File.WriteAllText(LedgerPath(), "{ not a ledger");
        var settler = new LedgerSkipSettler(new FileLedgerStore(LedgerPath()));

        var error = Assert.Throws<InvalidOperationException>(() => settler.Settle(new SkipCommit(Skip())));

        Assert.Contains("ledger", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{ not a ledger", File.ReadAllText(LedgerPath()));
    }
}
