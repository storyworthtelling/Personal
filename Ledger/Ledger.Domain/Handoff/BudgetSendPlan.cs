namespace Index.Domain.Handoff;

public enum BudgetSendDecision
{
    Nothing,
    Send,
}

/// <summary>What happened when a send was tried.</summary>
public enum BudgetSendOutcome
{
    /// <summary>Constitution is not installed: the ordinary case, with no outbox and no retry queue.</summary>
    ReceiverAbsent,
    Sent,
    /// <summary>The platform refused the broadcast. Quiet like absence; the next change sends again.</summary>
    SendFailed,
    /// <summary>
    /// Index does not hold RECEIVE_BUDGET (installed before Constitution declared it, say), so the receiver would
    /// drop the broadcast without a word. Not a send; the next refresh tries again once the grant is there.
    /// </summary>
    NotPermitted,
}

/// <summary>
/// When Index sends the budget, as pure decisions. It sends on a change from the last payload actually
/// delivered to an installed receiver, and a first run (nothing recorded) sends. Constitution being absent is
/// the ordinary case: nothing is queued, nothing is reported, and the record only moves when a send was
/// really attempted to an installed receiver, so installing Constitution later gets the current figures on
/// the next refresh.
/// </summary>
public static class BudgetSendPlan
{
    /// <summary>
    /// Where the last payload delivered is kept. A new key, once: until audit W2 Constitution did not hold
    /// RECEIVE_BUDGET, so every send recorded under <see cref="RetiredRecordKey"/> reached nobody, and keeping
    /// that record would hold the current month back until its figures next changed.
    /// </summary>
    public const string RecordKey = "ConstitutionBudgetLastDelivered";

    /// <summary>The record kept before audit W2, removed on the next send.</summary>
    public const string RetiredRecordKey = "ConstitutionBudgetLastSent";

    public static BudgetSendDecision Decide(ConstitutionBudgetPayload? previous, ConstitutionBudgetPayload? current)
    {
        if (current is null)
        {
            return BudgetSendDecision.Nothing;
        }

        return current.Equals(previous) ? BudgetSendDecision.Nothing : BudgetSendDecision.Send;
    }

    public static BudgetSendOutcome Outcome(bool receiverInstalled, bool senderHoldsPermission, bool sendThrew)
    {
        if (!receiverInstalled)
        {
            return BudgetSendOutcome.ReceiverAbsent;
        }

        if (!senderHoldsPermission)
        {
            return BudgetSendOutcome.NotPermitted;
        }

        return sendThrew ? BudgetSendOutcome.SendFailed : BudgetSendOutcome.Sent;
    }

    /// <summary>Whether the last-sent record moves. Only a broadcast that went out to an installed receiver
    /// counts; a failed send is retried because the record did not advance.</summary>
    public static bool AdvancesRecord(BudgetSendOutcome outcome) => outcome == BudgetSendOutcome.Sent;

    /// <summary>No outcome is a fault to show the owner: none is voiced, noticed or queued.</summary>
    public static bool IsFault(BudgetSendOutcome outcome) => false;
}
