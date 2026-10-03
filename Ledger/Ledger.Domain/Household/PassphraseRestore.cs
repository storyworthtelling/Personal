namespace Index.Domain.Household;

/// <summary>
/// Lets one vault restore run at a time, process-wide. The launch and a passphrase save can both want
/// one, and two at once would race on the same institutions table. A caller that arrives while one is in
/// flight is refused and may wait on <see cref="InFlight"/> instead; it never starts a second.
/// </summary>
public sealed class RestoreGate
{
    private int running;
    private Task? inFlight;

    public bool IsRunning => Volatile.Read(ref running) == 1;

    /// <summary>The run in progress, or null when none is. Lets something that must come after a restore
    /// (publishing the vault) wait for it.</summary>
    public Task? InFlight => IsRunning ? Volatile.Read(ref inFlight) : null;

    /// <summary>Starts <paramref name="work"/> unless a run is already in flight. The gate is closed
    /// before this returns, so whoever looks next sees a restore under way. The returned task carries the
    /// work's own exception (original stack trace intact); the gate is freed whether it succeeds or not.
    /// Null means another run holds the gate and nothing was started.</summary>
    public Task? TryStart(Func<Task> work)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            return null;
        }

        Task started;
        try
        {
            started = work();
        }
        catch (Exception ex)
        {
            started = Task.FromException(ex);
        }

        var guarded = Release(started);
        if (!guarded.IsCompleted)
        {
            Volatile.Write(ref inFlight, guarded);
        }

        return guarded;
    }

    private async Task Release(Task work)
    {
        try
        {
            await work;
        }
        finally
        {
            Volatile.Write(ref inFlight, null);
            Volatile.Write(ref running, 0);
        }
    }
}

/// <summary>
/// What saving the vault passphrase does (task 2.6, R-G22). Restore used to run only at launch, so a
/// passphrase typed afterwards brought nothing back and Accounts offered Add over a vault full of banks.
/// It is the launch plan, run when the passphrase CHANGES: once per change (saving the same value again
/// restores nothing), and only on an install that may touch Plaid.
/// </summary>
public sealed record PassphraseSavePlan(
    bool Restore, bool ScheduleRefresh, bool RunRefreshNow, bool ScheduleWorksheet, bool CancelPlaidWork)
{
    public static PassphraseSavePlan For(string? before, string? after, InstallRole role)
    {
        // Hers never restores or schedules; any Plaid work the launch left queued is withdrawn.
        if (!InstallRoleGate.MayRunPlaidWork(role))
        {
            return new PassphraseSavePlan(false, false, false, false, CancelPlaidWork: true);
        }

        var changed = !string.IsNullOrWhiteSpace(after)
            && !string.Equals(before ?? string.Empty, after, StringComparison.Ordinal);

        return changed
            ? new PassphraseSavePlan(true, true, true, true, CancelPlaidWork: false)
            : new PassphraseSavePlan(false, false, false, false, CancelPlaidWork: false);
    }
}

/// <summary>The device-side effects of owning the Plaid work, behind a seam so the order they happen in
/// can be tested without Android.</summary>
public interface IOwnerWorkScheduler
{
    /// <summary>Brings the Plaid credentials and every bank back from the vault.</summary>
    Task RestoreAsync(CancellationToken cancellationToken);

    /// <summary>Whether Plaid credentials are present now. Read after a restore, which is what brings
    /// them back.</summary>
    bool IsConfigured { get; }

    void ScheduleRefresh();
    void CancelRefresh();
    void RunRefreshNow();
    void ScheduleWorksheet();
    void CancelWorksheet();
}

/// <summary>The outcome of asking for a restore. <see cref="Completion"/> is the run the caller may wait
/// on: its own when <see cref="Started"/>, the one already in flight when another holds the gate, and an
/// already-completed task when there was nothing to run. It never throws; failures go to the flow's
/// callback.</summary>
public sealed record RestoreRun(bool Started, Task Completion);

/// <summary>
/// Settle the role, then — on an install that may touch Plaid, when the passphrase changed — restore,
/// schedule the refresh, run it now if there are credentials, and schedule the worksheet worker: what the
/// launch would have done had the passphrase been there. Her install does none of it.
/// </summary>
public sealed class PassphraseRestoreFlow
{
    private readonly InstallRoleService roles;
    private readonly RestoreGate gate;
    private readonly IOwnerWorkScheduler work;
    private readonly Action<Exception> onFailed;

    public PassphraseRestoreFlow(
        InstallRoleService roles, RestoreGate gate, IOwnerWorkScheduler work, Action<Exception> onFailed)
    {
        this.roles = roles;
        this.gate = gate;
        this.work = work;
        this.onFailed = onFailed;
    }

    /// <summary>Returns as soon as the restore has STARTED (the gate is then closed), not when it ends,
    /// so the caller can go back to Accounts, which shows the restoring state while the gate is shut.</summary>
    public async Task<RestoreRun> BeginAsync(string? before, string? after, CancellationToken cancellationToken)
    {
        var role = await roles.ResolveAsync(cancellationToken);
        var plan = PassphraseSavePlan.For(before, after, role);

        if (plan.CancelPlaidWork)
        {
            work.CancelRefresh();
            work.CancelWorksheet();
            return new RestoreRun(false, Task.CompletedTask);
        }

        if (!plan.Restore)
        {
            return new RestoreRun(false, gate.InFlight ?? Task.CompletedTask);
        }

        var started = gate.TryStart(() => RunAsync(plan, cancellationToken));
        return started is null
            ? new RestoreRun(false, gate.InFlight ?? Task.CompletedTask)
            : new RestoreRun(true, started);
    }

    private async Task RunAsync(PassphraseSavePlan plan, CancellationToken cancellationToken)
    {
        try
        {
            await work.RestoreAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Restore is a convenience (Settings always accepts the credentials by hand), but the failure
            // is reported, not dropped; the owner's workers are still scheduled, as at launch.
            onFailed(ex);
        }

        try
        {
            if (plan.ScheduleRefresh)
            {
                work.ScheduleRefresh();
            }

            if (plan.RunRefreshNow && work.IsConfigured)
            {
                work.RunRefreshNow();
            }

            if (plan.ScheduleWorksheet)
            {
                work.ScheduleWorksheet();
            }
        }
        catch (Exception ex)
        {
            onFailed(ex);
        }
    }
}
