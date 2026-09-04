using Codex.Domain.Unlock;
using Xunit;

namespace Codex.Tests.Unlock;

public class UnlockStateMachineTests
{
    private readonly FakeClock clock = new();

    private UnlockStateMachine NewMachine() => new(clock);

    [Fact]
    public void NewMachine_StartsLocked()
    {
        Assert.Equal(UnlockState.Locked, NewMachine().CurrentState);
    }

    [Fact]
    public void RequestUnlock_EntersPendingWithFullDelayRemaining()
    {
        var machine = NewMachine();
        machine.RequestUnlock();

        Assert.Equal(UnlockState.Pending, machine.CurrentState);
        Assert.Equal(UnlockStateMachine.UnlockDelay, machine.RemainingDelay);
    }

    [Fact]
    public void CancelRequest_ReturnsToLocked()
    {
        var machine = NewMachine();
        machine.RequestUnlock();
        clock.Advance(TimeSpan.FromHours(2));

        machine.CancelRequest();

        Assert.Equal(UnlockState.Locked, machine.CurrentState);
    }

    [Fact]
    public void CancelThenRequestAgain_RestartsCountdownFromZero()
    {
        var machine = NewMachine();
        machine.RequestUnlock();
        clock.Advance(TimeSpan.FromHours(2));
        machine.CancelRequest();

        machine.RequestUnlock();

        Assert.Equal(UnlockStateMachine.UnlockDelay, machine.RemainingDelay);
    }

    [Fact]
    public void Pending_BeforeDelayElapses_StaysPending()
    {
        var machine = NewMachine();
        machine.RequestUnlock();

        clock.Advance(UnlockStateMachine.UnlockDelay - TimeSpan.FromMinutes(1));

        Assert.Equal(UnlockState.Pending, machine.CurrentState);
    }

    [Fact]
    public void Pending_AfterDelayElapses_BecomesReady()
    {
        var machine = NewMachine();
        machine.RequestUnlock();

        clock.Advance(UnlockStateMachine.UnlockDelay);

        Assert.Equal(UnlockState.Ready, machine.CurrentState);
    }

    [Fact]
    public void RemainingDelay_ReflectsElapsedTime()
    {
        var machine = NewMachine();
        machine.RequestUnlock();

        clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(UnlockStateMachine.UnlockDelay - TimeSpan.FromHours(2), machine.RemainingDelay);
    }

    [Fact]
    public void EnterAdminMode_WhenReady_BecomesAdminActive()
    {
        var machine = NewMachine();
        machine.RequestUnlock();
        clock.Advance(UnlockStateMachine.UnlockDelay);

        machine.EnterAdminMode();

        Assert.Equal(UnlockState.AdminActive, machine.CurrentState);
    }

    [Fact]
    public void EnterAdminMode_WhenLocked_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => NewMachine().EnterAdminMode());
    }

    [Fact]
    public void EnterAdminMode_WhenStillPending_Throws()
    {
        var machine = NewMachine();
        machine.RequestUnlock();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Throws<InvalidOperationException>(() => machine.EnterAdminMode());
    }

    [Fact]
    public void AdminMode_ExpiresAfterAdminDuration()
    {
        var machine = NewMachine();
        machine.RequestUnlock();
        clock.Advance(UnlockStateMachine.UnlockDelay);
        machine.EnterAdminMode();

        clock.Advance(UnlockStateMachine.AdminDuration);

        Assert.Equal(UnlockState.Locked, machine.CurrentState);
    }

    [Fact]
    public void AdminMode_BeforeExpiry_StaysActive()
    {
        var machine = NewMachine();
        machine.RequestUnlock();
        clock.Advance(UnlockStateMachine.UnlockDelay);
        machine.EnterAdminMode();

        clock.Advance(UnlockStateMachine.AdminDuration - TimeSpan.FromMinutes(1));

        Assert.Equal(UnlockState.AdminActive, machine.CurrentState);
    }

    [Fact]
    public void Relock_FromAdminActive_ReturnsToLocked()
    {
        var machine = NewMachine();
        machine.RequestUnlock();
        clock.Advance(UnlockStateMachine.UnlockDelay);
        machine.EnterAdminMode();

        machine.Relock();

        Assert.Equal(UnlockState.Locked, machine.CurrentState);
    }

    [Fact]
    public void Ready_DoesNotExpireBackToLocked()
    {
        var machine = NewMachine();
        machine.RequestUnlock();
        clock.Advance(UnlockStateMachine.UnlockDelay + TimeSpan.FromDays(5));

        Assert.Equal(UnlockState.Ready, machine.CurrentState);
    }

    [Fact]
    public void ExportedData_RestoresStateInNewInstance()
    {
        var machine = NewMachine();
        machine.RequestUnlock();
        clock.Advance(TimeSpan.FromHours(2));
        var persisted = machine.Export();

        var restored = new UnlockStateMachine(clock, persisted);

        Assert.Equal(UnlockState.Pending, restored.CurrentState);
        Assert.Equal(UnlockStateMachine.UnlockDelay - TimeSpan.FromHours(2), restored.RemainingDelay);
    }

    [Fact]
    public void RestoredMachine_CompletesCountdownAcrossRestart()
    {
        var machine = NewMachine();
        machine.RequestUnlock();
        clock.Advance(TimeSpan.FromHours(2));

        var restored = new UnlockStateMachine(clock, machine.Export());
        clock.Advance(UnlockStateMachine.UnlockDelay - TimeSpan.FromHours(2));

        Assert.Equal(UnlockState.Ready, restored.CurrentState);
    }

    [Fact]
    public void BackwardsClockJump_DoesNotAddProgress()
    {
        var machine = NewMachine();
        machine.RequestUnlock();
        clock.Advance(TimeSpan.FromHours(4));
        Assert.Equal(UnlockState.Pending, machine.CurrentState);

        // A backwards clock jump adds no progress and keeps the countdown paused (fail-closed).
        clock.Advance(TimeSpan.FromHours(-2));
        Assert.Equal(UnlockState.Pending, machine.CurrentState);

        // Forward again, resuming from the accumulated 4h — one minute short of the delay.
        clock.Advance(UnlockStateMachine.UnlockDelay - TimeSpan.FromHours(4) - TimeSpan.FromMinutes(1));
        Assert.Equal(UnlockState.Pending, machine.CurrentState);
        Assert.Equal(TimeSpan.FromMinutes(1), machine.RemainingDelay);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(UnlockState.Ready, machine.CurrentState);
    }

    [Fact]
    public void BackwardsClockJump_DuringAdminMode_RelocksImmediately()
    {
        var machine = NewMachine();
        machine.RequestUnlock();
        clock.Advance(UnlockStateMachine.UnlockDelay);
        machine.EnterAdminMode();

        clock.Advance(TimeSpan.FromMinutes(-5));

        Assert.Equal(UnlockState.Locked, machine.CurrentState);
    }

    [Fact]
    public void RemainingDelay_IsNullOutsidePending()
    {
        var machine = NewMachine();
        Assert.Null(machine.RemainingDelay);

        machine.RequestUnlock();
        clock.Advance(UnlockStateMachine.UnlockDelay);
        Assert.Null(machine.RemainingDelay);
    }
}
