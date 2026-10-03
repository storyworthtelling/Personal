using Index.Domain.Household;
using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Household;

/// <summary>
/// Task 2.6 of the remediation plan (mockup 14, first run). R-G22 extends G26: once the passphrase has
/// been saved on an install that may touch Plaid, the banks come back without a restart and the page
/// never offers Add in between; restore runs exactly once per passphrase change, never twice at the
/// same moment, and never on her install.
///
/// <para>The Android head cannot be loaded by this desktop-TFM project, so the decisions it makes are
/// pure Domain functions tested here, and <c>FirstRunSourceTests</c> reads the head to prove it obeys
/// them.</para>
/// </summary>
public class FirstRunTests
{
    private const string OwnerEmail = "owner@example.com";
    private const string HerEmail = "her@example.com";
    private const string Passphrase = "correct horse battery staple";

    // ---- the three screens (D107, D158) ----

    [Theory]
    [InlineData(false, false, false, FirstRunScreen.NothingConnected)]
    [InlineData(false, true, false, FirstRunScreen.VaultHoldsBanks)]
    [InlineData(true, false, false, FirstRunScreen.NothingConnected)]
    [InlineData(true, true, true, FirstRunScreen.Restoring)]
    [InlineData(true, false, true, FirstRunScreen.Restoring)]
    [InlineData(false, true, true, FirstRunScreen.Restoring)]
    public void TheFirstRunScreenIsChosenFromTheThreeFacts(
        bool passphraseSet, bool vaultHoldsSomething, bool restoreRunning, FirstRunScreen expected)
    {
        Assert.Equal(expected, FirstRunPlan.Decide(passphraseSet, vaultHoldsSomething, restoreRunning));
    }

    [Theory]
    [InlineData(FirstRunScreen.NothingConnected, true)]
    [InlineData(FirstRunScreen.VaultHoldsBanks, false)]
    [InlineData(FirstRunScreen.Restoring, false)]
    public void AddIsOfferedOnlyWhenNothingIsConnectedAndNothingIsComingBack(FirstRunScreen screen, bool offersAdd)
    {
        Assert.Equal(offersAdd, FirstRunPlan.OffersAdd(screen));
    }

    // ---- state 3: the page fills in place, a bank never vanishes (finding 2) ----

    private static Institution Bank(
        string name, WorkStatus status = WorkStatus.Pending, DateTimeOffset? refreshed = null, int accounts = 0,
        string token = "access-token")
    {
        var institution = new Institution
        {
            Name = name,
            ItemId = "item-" + name,
            AccessToken = token,
            RefreshStatus = status,
            LastRefreshedUtc = refreshed,
        };
        for (var i = 0; i < accounts; i++)
        {
            institution.Accounts.Add(new Account { Name = name + i, Kind = AccountKind.Depository, CurrentBalance = 10m });
        }

        return institution;
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenEveryRestoredBankIsStillPendingTheStatusLineCountsThemInAndAddIsNotOffered()
    {
        var banks = Enumerable.Range(1, 6).Select(n => Bank("Bank" + n)).ToList();

        var progress = RestoreProgress.Of(banks);

        Assert.True(progress.Settling);
        Assert.Equal("Bringing back 6 banks · 0 so far", progress.StatusLine);
        Assert.False(RestoreProgress.OffersAdd(banks));
        Assert.All(banks, b => Assert.True(RestoreProgress.AwaitsFirstFigures(b)));
    }

    [Fact]
    public void AsBanksAnswerTheCountMovesAndAddComesBackOnceThereIsSomethingToSee()
    {
        var banks = new List<Institution>
        {
            Bank("Redwood Bank", WorkStatus.Completed, Now, accounts: 2),
            Bank("Meridian Card", WorkStatus.Completed, Now, accounts: 1),
            Bank("PNC"), Bank("Amex"), Bank("Citi"), Bank("Ally"),
        };

        var progress = RestoreProgress.Of(banks);

        Assert.Equal("Bringing back 6 banks · 2 so far", progress.StatusLine);
        Assert.True(RestoreProgress.OffersAdd(banks));
    }

    [Fact]
    public void AFailedFirstRefreshCountsAsAnsweredButKeepsItsPlaceWithAPlainLine()
    {
        var failed = Bank("PNC", WorkStatus.Failed);
        var banks = new List<Institution> { Bank("Redwood Bank", WorkStatus.Completed, Now, accounts: 1), failed };

        var progress = RestoreProgress.Of(banks);

        Assert.False(progress.Settling);
        Assert.Null(progress.StatusLine);
        Assert.True(RestoreProgress.AwaitsFirstFigures(failed));
        Assert.Equal("No balances yet.", RestoreProgress.PendingLine(failed));
        Assert.Equal("Waiting for its balances.", RestoreProgress.PendingLine(Bank("Amex")));
    }

    [Fact]
    public void AllBanksFailedIsNotRestoringSoAddIsOffered()
    {
        var banks = new List<Institution> { Bank("PNC", WorkStatus.Failed), Bank("Amex", WorkStatus.Failed) };

        Assert.False(RestoreProgress.Of(banks).Settling);
        Assert.True(RestoreProgress.OffersAdd(banks));
    }

    [Fact]
    public void ASettledAccountOrAHandEnteredBankIsNeverTreatedAsAwaitingFigures()
    {
        var paidOff = Bank("Redwood Bank", WorkStatus.Completed, Now);
        paidOff.Accounts.Add(new Account { Name = "Card", Kind = AccountKind.Credit, CurrentBalance = 0m });
        var handEntered = Bank("Mum", WorkStatus.Completed, Now, token: string.Empty);

        Assert.False(RestoreProgress.AwaitsFirstFigures(paidOff));
        Assert.False(RestoreProgress.AwaitsFirstFigures(handEntered));
    }

    [Fact]
    public void OnlyAnOpenAccountCountsAsSomethingToShow()
    {
        var pendingOnly = new List<Institution> { Bank("Redwood Bank") };
        var settledOnly = Bank("Amex", WorkStatus.Completed, Now);
        settledOnly.Accounts.Add(new Account { Name = "Card", Kind = AccountKind.Credit, CurrentBalance = 0m });

        Assert.False(RestoreProgress.HasCardsToShow(pendingOnly));
        Assert.False(RestoreProgress.HasCardsToShow([settledOnly]));
        Assert.True(RestoreProgress.HasCardsToShow([Bank("PNC", WorkStatus.Completed, Now, accounts: 1)]));
    }

    [Fact]
    public void ASingularBankReadsInTheSingular()
    {
        Assert.Equal("Bringing back 1 bank · 0 so far", RestoreProgress.Of([Bank("Redwood Bank")]).StatusLine);
    }

    [Fact]
    public void TheSignatureChangesWhenABankAnswersAndNotOtherwise()
    {
        var pending = new List<Institution> { Bank("Redwood Bank"), Bank("PNC") };
        var same = new List<Institution> { pending[0], pending[1] };
        Assert.Equal(RestoreProgress.Signature(pending, restoring: false), RestoreProgress.Signature(same, restoring: false));

        var answered = new List<Institution>
        {
            new() { Id = pending[0].Id, Name = "Redwood Bank", AccessToken = "t", RefreshStatus = WorkStatus.Completed,
                    LastRefreshedUtc = Now, Accounts = { new Account { Name = "x", CurrentBalance = 1m } } },
            pending[1],
        };

        Assert.NotEqual(RestoreProgress.Signature(pending, false), RestoreProgress.Signature(answered, false));
        Assert.NotEqual(RestoreProgress.Signature(pending, false), RestoreProgress.Signature(pending, true));
    }

    // ---- the save plan: who restores, and when (R-G22, R-G2) ----

    [Fact]
    public void ANewPassphraseOnAnOwnerInstallRestoresAndSchedulesWhatTheLaunchSkipped()
    {
        var plan = PassphraseSavePlan.For("", Passphrase, InstallRole.Owner);

        Assert.True(plan.Restore);
        Assert.True(plan.ScheduleRefresh);
        Assert.True(plan.RunRefreshNow);
        Assert.True(plan.ScheduleWorksheet);
        Assert.False(plan.CancelPlaidWork);
    }

    [Fact]
    public void HersNeverRestoresAndHasItsPlaidWorkCancelled()
    {
        var plan = PassphraseSavePlan.For("", Passphrase, InstallRole.Hers);

        Assert.False(plan.Restore);
        Assert.False(plan.ScheduleRefresh);
        Assert.False(plan.RunRefreshNow);
        Assert.False(plan.ScheduleWorksheet);
        Assert.True(plan.CancelPlaidWork);
    }

    [Theory]
    [InlineData("pw", "pw")]
    [InlineData(null, "")]
    [InlineData("old", "")]
    [InlineData("", "   ")]
    public void NoNewPassphraseMeansNothingToRestore(string? before, string? after)
    {
        var plan = PassphraseSavePlan.For(before, after, InstallRole.Owner);

        Assert.False(plan.Restore);
        Assert.False(plan.ScheduleRefresh);
        Assert.False(plan.CancelPlaidWork);
    }

    // ---- the gate: never twice at once ----

    [Fact]
    public async Task TheGateRefusesASecondRunWhileOneIsInFlightAndFreesItselfAfterwards()
    {
        var gate = new RestoreGate();
        var release = new TaskCompletionSource();
        var runs = 0;

        var first = gate.TryStart(async () => { runs++; await release.Task; });

        Assert.NotNull(first);
        Assert.True(gate.IsRunning);
        Assert.Null(gate.TryStart(() => { runs++; return Task.CompletedTask; }));

        release.SetResult();
        await first!;

        Assert.False(gate.IsRunning);
        Assert.Equal(1, runs);
        Assert.NotNull(gate.TryStart(() => Task.CompletedTask));
    }

    [Fact]
    public async Task TheGateIsFreedWhenTheWorkThrowsAndTheStackTraceSurvives()
    {
        var gate = new RestoreGate();

        var run = gate.TryStart(() => throw new InvalidOperationException("drive refused"));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => run!);
        Assert.Equal("drive refused", thrown.Message);
        Assert.False(gate.IsRunning);
    }

    // ---- the flow, end to end with fakes (R-G22) ----

    private sealed class MemoryCache : IInstallRoleCache
    {
        private InstallRole? value;
        public InstallRole? Get() => value;
        public void Set(InstallRole role) => value = role;
        public void Clear() => value = null;
    }

    private sealed class Lookup(HouseholdFilePublisher answer) : IHouseholdPublisherLookup
    {
        public Task<HouseholdFilePublisher> LookAsync(string passphrase, CancellationToken cancellationToken) =>
            Task.FromResult(answer);
    }

    private sealed class FakeWork : IOwnerWorkScheduler
    {
        public int Restores { get; private set; }
        public int RefreshSchedules { get; private set; }
        public int RefreshCancels { get; private set; }
        public int RunNows { get; private set; }
        public int WorksheetSchedules { get; private set; }
        public int WorksheetCancels { get; private set; }
        public bool Configured { get; set; }
        public bool ConfiguredByRestore { get; set; } = true;
        public TaskCompletionSource? Hold { get; set; }
        public Exception? Failure { get; set; }

        public bool IsConfigured => Configured;

        public async Task RestoreAsync(CancellationToken cancellationToken)
        {
            Restores++;
            if (Hold is not null)
            {
                await Hold.Task;
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            // A restore is what brings the Plaid credentials back, so the refresh can only run after it.
            Configured = ConfiguredByRestore;
        }

        public void ScheduleRefresh() => RefreshSchedules++;
        public void CancelRefresh() => RefreshCancels++;
        public void RunRefreshNow() => RunNows++;
        public void ScheduleWorksheet() => WorksheetSchedules++;
        public void CancelWorksheet() => WorksheetCancels++;
    }

    private static (PassphraseRestoreFlow Flow, RestoreGate Gate, FakeWork Work, List<Exception> Failures) Build(
        HouseholdFilePublisher household, string account = OwnerEmail)
    {
        var gate = new RestoreGate();
        var work = new FakeWork();
        var failures = new List<Exception>();
        var roles = new InstallRoleService(new MemoryCache(), new Lookup(household), () => account, () => Passphrase);
        return (new PassphraseRestoreFlow(roles, gate, work, failures.Add), gate, work, failures);
    }

    [Fact]
    public async Task SavingThePassphraseOnTheOwnersInstallRestoresOnceThenSchedulesAndRefreshes()
    {
        var (flow, gate, work, failures) = Build(HouseholdFilePublisher.Absent);
        work.Hold = new TaskCompletionSource();

        var run = await flow.BeginAsync("", Passphrase, default);

        // The gate is already closed when BeginAsync returns, so the Accounts page that comes back to the
        // foreground while the restore is still under way sees "restoring" and never "Nothing connected
        // yet" with an Add button: the double-link G26 exists to prevent.
        Assert.True(run.Started);
        Assert.True(gate.IsRunning);
        var screen = FirstRunPlan.Decide(passphraseSet: true, vaultHoldsSomething: true, restoreRunning: gate.IsRunning);
        Assert.Equal(FirstRunScreen.Restoring, screen);
        Assert.False(FirstRunPlan.OffersAdd(screen));
        Assert.Equal(0, work.RefreshSchedules);

        work.Hold.SetResult();
        await run.Completion;

        Assert.Equal(1, work.Restores);
        Assert.Equal(1, work.RefreshSchedules);
        Assert.Equal(1, work.RunNows);
        Assert.Equal(1, work.WorksheetSchedules);
        Assert.Equal(0, work.RefreshCancels);
        Assert.Empty(failures);
        Assert.False(gate.IsRunning);
    }

    [Fact]
    public async Task TheRefreshIsNotRunNowWhenTheRestoreLeftNoPlaidCredentials()
    {
        var (flow, _, work, _) = Build(HouseholdFilePublisher.Absent);
        work.ConfiguredByRestore = false;

        await (await flow.BeginAsync("", Passphrase, default)).Completion;

        Assert.Equal(1, work.RefreshSchedules);
        Assert.Equal(0, work.RunNows);
    }

    [Fact]
    public async Task SavingTheSamePassphraseAgainDoesNotRestoreAgain()
    {
        var (flow, _, work, _) = Build(HouseholdFilePublisher.Absent);

        await (await flow.BeginAsync("", Passphrase, default)).Completion;
        var again = await flow.BeginAsync(Passphrase, Passphrase, default);

        Assert.False(again.Started);
        Assert.Equal(1, work.Restores);
    }

    [Fact]
    public async Task ChangingThePassphraseAgainRestoresAgainOnceTheFirstRunHasFinished()
    {
        var (flow, _, work, _) = Build(HouseholdFilePublisher.Absent);

        await (await flow.BeginAsync("", Passphrase, default)).Completion;
        await (await flow.BeginAsync(Passphrase, Passphrase + "2", default)).Completion;

        Assert.Equal(2, work.Restores);
    }

    [Fact]
    public async Task ARestoreInFlightIsNeverJoinedByASecondOne()
    {
        var (flow, gate, work, _) = Build(HouseholdFilePublisher.Absent);
        work.Hold = new TaskCompletionSource();

        var first = await flow.BeginAsync("", Passphrase, default);
        var second = await flow.BeginAsync(Passphrase, Passphrase + "2", default);

        Assert.True(first.Started);
        Assert.False(second.Started);
        Assert.True(gate.IsRunning);
        Assert.Equal(1, work.Restores);
        // The second caller is handed the run already in flight, so anything that must wait for the
        // restore (publishing the vault) waits for the real one rather than racing it.
        Assert.False(second.Completion.IsCompleted);

        work.Hold.SetResult();
        await first.Completion;
        await second.Completion;

        Assert.Equal(1, work.Restores);
        Assert.False(gate.IsRunning);
    }

    [Fact]
    public async Task HersSkipsTheRestoreAndHasItsWorkersCancelled()
    {
        var (flow, gate, work, _) = Build(HouseholdFilePublisher.PublishedBy(OwnerEmail), account: HerEmail);

        var run = await flow.BeginAsync("", Passphrase, default);
        await run.Completion;

        Assert.False(run.Started);
        Assert.False(gate.IsRunning);
        Assert.Equal(0, work.Restores);
        Assert.Equal(0, work.RefreshSchedules);
        Assert.Equal(0, work.RunNows);
        Assert.Equal(0, work.WorksheetSchedules);
        Assert.Equal(1, work.RefreshCancels);
        Assert.Equal(1, work.WorksheetCancels);
    }

    [Fact]
    public async Task AFailedRestoreIsReportedNotSwallowedAndStillLeavesTheOwnerScheduled()
    {
        var (flow, gate, work, failures) = Build(HouseholdFilePublisher.Absent);
        work.Failure = new InvalidOperationException("wrong passphrase");

        var run = await flow.BeginAsync("", Passphrase, default);
        await run.Completion; // never throws: the failure goes to the callback

        var failure = Assert.Single(failures);
        Assert.Equal("wrong passphrase", failure.Message);
        Assert.Equal(1, work.RefreshSchedules);
        Assert.False(gate.IsRunning);
    }
}
