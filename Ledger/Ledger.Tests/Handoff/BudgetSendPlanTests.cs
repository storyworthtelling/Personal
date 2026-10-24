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
        var outcome = BudgetSendPlan.Outcome(receiverInstalled: false, sendThrew: false);

        Assert.Equal(BudgetSendOutcome.ReceiverAbsent, outcome);
        Assert.False(BudgetSendPlan.AdvancesRecord(outcome));
        Assert.False(BudgetSendPlan.IsFault(outcome));
    }

    [Fact]
    public void ASendToAnInstalledReceiverAdvancesTheRecord()
    {
        var sent = BudgetSendPlan.Outcome(receiverInstalled: true, sendThrew: false);
        Assert.True(BudgetSendPlan.AdvancesRecord(sent));

        var failed = BudgetSendPlan.Outcome(receiverInstalled: true, sendThrew: true);
        Assert.False(BudgetSendPlan.AdvancesRecord(failed));
        Assert.False(BudgetSendPlan.IsFault(failed));   // quiet: no outbox, no notice
    }
}
