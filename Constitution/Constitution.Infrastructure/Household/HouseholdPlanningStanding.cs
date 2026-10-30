using Constitution.Domain.Household;
using Constitution.Domain.Household.Lease;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Household;

/// <summary>The answer to "may this install plan this week?", with the decision behind it and a plain sentence for it.</summary>
public sealed record PlanningStanding(bool MayPlan, LeaseDecision? Decision, string Sentence);

/// <summary>
/// What the weekly runs ask before they plan (C-6.1.3): this install's lease standing for the week that contains now, read from the
/// phone's copy of the household file. <see cref="AskAsync"/> writes nothing. <see cref="ClaimAsync"/> does what the decision asked
/// (a claim for an unclaimed week, a release of an old one) and asks again, so the run takes the week as it opens and plans once the
/// claim has settled. No planner calls this yet. An unknown role never plans: the lease has no unknown role, so it is not asked, and
/// a file that will not open is the library's null document, which fails closed.
/// </summary>
public sealed class HouseholdPlanningStanding(HouseholdFileService service)
{
    public const string RoleUnknownSentence = "The household file has not said whose phone this is yet, so this phone does not plan the week.";

    public async Task<PlanningStanding> AskAsync(CancellationToken cancellationToken)
    {
        var role = service.Role;
        if (role == HouseholdFileRole.Unknown)
        {
            return new PlanningStanding(false, null, RoleUnknownSentence);
        }

        HouseholdDocument? document = null;
        if (!service.Standing.IsUnreadable)
        {
            try
            {
                document = await service.ReadAsync(cancellationToken);
            }
            catch (HouseholdFormatException)
            {
                // The phone's own copy is damaged: the null document below is the library's way of saying so, and it fails closed.
                document = null;
            }
        }

        var decision = PlanningLease.Decide(
            document, service.InstallId, RosterEditor.RoleFor(role), service.Now, service.Zone, LeaseTerms.DefaultLeaseLength);
        return new PlanningStanding(decision.MayPlan, decision, SentenceFor(decision));
    }

    public async Task<PlanningStanding> ClaimAsync(CancellationToken cancellationToken)
    {
        var asked = await AskAsync(cancellationToken);
        if (asked.Decision is not { Verdict: LeaseVerdict.Take or LeaseVerdict.Release } decision)
        {
            return asked;
        }

        var rosterRole = service.RosterRole;
        var now = service.Now;
        var zone = service.Zone;
        await service.EditAsync(
            (document, clock, writer) => LeaseWriter.Apply(document, decision, clock, writer, rosterRole, now, zone, LeaseTerms.DefaultLeaseLength),
            cancellationToken);
        return await AskAsync(cancellationToken);
    }

    private static string SentenceFor(LeaseDecision decision) => decision.Verdict switch
    {
        LeaseVerdict.Plan => "This phone plans this week.",
        LeaseVerdict.Take => "This phone may take this week; it plans once the claim has settled.",
        LeaseVerdict.Release => "An old claim is being let go; this phone does not plan until it is.",
        _ => decision.Reason switch
        {
            LeaseReason.DocumentUnreadable => "The household file cannot be read, so this phone does not plan the week.",
            LeaseReason.HeldByAnother => "The other phone holds this week's planning.",
            LeaseReason.ClaimNotSettled => "This phone has taken the week; it plans once the claim has stood long enough.",
            LeaseReason.OwnerNeverSeen => "This phone has not heard from the owner's phone yet, so it does not plan the week.",
            _ => "This phone does not plan this week yet.",
        },
    };
}
