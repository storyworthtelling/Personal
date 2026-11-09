using Index.Domain.Handoff;
using Xunit;

namespace Index.Tests.Handoff;

public class BudgetSendPlanTests
{
    private static ConstitutionBudgetPayload Make(string month = "2026-10", decimal budget = 400m, decimal spent = 120m) =>
        ConstitutionBudgetPayload.TryCreate(month, budget, spent)!;

    [Fact]
    public void SendsOnlyOnChange()
    {
        Assert.Equal(BudgetSendDecision.Send, BudgetSendPlan.Decide(null, Make()));
        Assert.Equal(BudgetSendDecision.Nothing, BudgetSendPlan.Decide(Make(), Make()));
        Assert.Equal(BudgetSendDecision.Send, BudgetSendPlan.Decide(Make(), Make(spent: 130m)));
        Assert.Equal(BudgetSendDecision.Send, BudgetSendPlan.Decide(Make(), Make(budget: 450m)));
        Assert.Equal(BudgetSendDecision.Send, BudgetSendPlan.Decide(Make(), Make(month: "2026-11")));
    }

    [Fact]
    public void NothingToSendIsNothing()
    {
        Assert.Equal(BudgetSendDecision.Nothing, BudgetSendPlan.Decide(Make(), null));
    }

    [Fact]
    public void AbsentReceiverIsOrdinary()
    {
        var outcome = BudgetSendPlan.Outcome(receiverInstalled: false, senderHoldsPermission: true, sendThrew: false);

        Assert.Equal(BudgetSendOutcome.ReceiverAbsent, outcome);
        Assert.False(BudgetSendPlan.AdvancesRecord(outcome));
        Assert.False(BudgetSendPlan.IsFault(outcome));
    }

    [Fact]
    public void ASendToAnInstalledReceiverAdvancesTheRecord()
    {
        var sent = BudgetSendPlan.Outcome(receiverInstalled: true, senderHoldsPermission: true, sendThrew: false);
        Assert.True(BudgetSendPlan.AdvancesRecord(sent));

        var failed = BudgetSendPlan.Outcome(receiverInstalled: true, senderHoldsPermission: true, sendThrew: true);
        Assert.False(BudgetSendPlan.AdvancesRecord(failed));
        Assert.False(BudgetSendPlan.IsFault(failed));   // quiet: no outbox, no notice
    }

    // A broadcast to a guarded receiver from an app without the permission is dropped by the platform with no
    // error, so "sent" would be a lie and the record would stop the month from ever going across again.
    [Fact]
    public void ASendWithoutThePermissionIsNotASend()
    {
        var outcome = BudgetSendPlan.Outcome(receiverInstalled: true, senderHoldsPermission: false, sendThrew: false);

        Assert.Equal(BudgetSendOutcome.NotPermitted, outcome);
        Assert.False(BudgetSendPlan.AdvancesRecord(outcome));
        Assert.False(BudgetSendPlan.IsFault(outcome));
    }

    // Until audit W2 Constitution did not hold RECEIVE_BUDGET, so every recorded send reached nobody. The record
    // moves to a new key once, so the current month goes across on the first refresh after the update.
    [Fact]
    public void TheRecordStartsAgainUnderANewKey()
    {
        Assert.Equal("ConstitutionBudgetLastSent", BudgetSendPlan.RetiredRecordKey);
        Assert.NotEqual(BudgetSendPlan.RetiredRecordKey, BudgetSendPlan.RecordKey);
    }

    [Fact]
    public void TheSenderReadsTheRecordByTheNewKeyAndChecksItsPermission()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Ledger", "Ledger.Android", "ConstitutionBudgetSender.cs"));

        Assert.Contains("Preferences.Get(BudgetSendPlan.RecordKey,", source, StringComparison.Ordinal);
        Assert.Contains("Preferences.Set(BudgetSendPlan.RecordKey,", source, StringComparison.Ordinal);
        Assert.Contains("Preferences.Remove(BudgetSendPlan.RetiredRecordKey)", source, StringComparison.Ordinal);
        Assert.Contains("CheckSelfPermission(ConstitutionBudgetContract.Permission)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ConstitutionBudgetLastSent\"", source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
