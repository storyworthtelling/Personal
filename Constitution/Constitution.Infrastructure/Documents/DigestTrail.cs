namespace Constitution.Infrastructure.Documents;

/// <summary>One value-free phrase per outcome of the Saturday digest for the trail: words about what happened, never a name, a figure or a Body value.</summary>
public static class DigestTrail
{
    public static string LogPhrase(DigestOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome switch
        {
            DigestOutcome.NotDue => "digest not due",
            DigestOutcome.AlreadyDelivered => "digest already sent",
            DigestOutcome.NotReady => "digest waiting on a store",
            DigestOutcome.Delivered => "digest sent",
            DigestOutcome.NotDelivered => "digest not sent, tablet refused",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "This outcome has no trail words; add them here."),
        };
    }
}
