using Microsoft.Extensions.Logging;
using Index.Domain.External;
using Index.Domain.Model;

namespace Index.Infrastructure.Services;

// The once-only fetch of a bank's Plaid primary_color (D127), shared by the two places it happens: right
// after a bank is linked, and as a backfill on the balance refresh for banks that were connected before
// anything fetched it. It mutates the institution it is given and leaves saving to the caller, because
// the refresh already saves the institution in one write and the link page saves through AccountService.
//
// "Once" is the whole point and is carried by ColorLookedUpAtUtc: set whenever Plaid ANSWERED (with or
// without a colour), left null when the question failed. A lookup that fails is logged and swallowed — a
// post-it staying the default yellow must never cost anyone their balances — and is asked again at the
// next refresh. Only cancellation escapes.
public static class InstitutionColorLookup
{
    // A Plaid-connected bank (hand-entered banks have an empty AccessToken and nothing to ask Plaid
    // about) that has neither a colour nor a recorded answer.
    public static bool IsDue(Institution institution) =>
        institution.AccessToken != string.Empty
        && string.IsNullOrWhiteSpace(institution.PrimaryColorHex)
        && institution.ColorLookedUpAtUtc is null;

    public static async Task ResolveAsync(
        Institution institution, IPlaidClient plaid, DateTimeOffset now, ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!IsDue(institution))
            return;

        try
        {
            var plaidInstitutionId = institution.PlaidInstitutionId;
            if (string.IsNullOrEmpty(plaidInstitutionId))
            {
                // Connected before the institution id was captured (or restored from the vault, which
                // carries only the Item's credentials): ask Plaid which bank this Item belongs to.
                plaidInstitutionId = await plaid.GetItemInstitutionIdAsync(institution.AccessToken, cancellationToken);
                if (string.IsNullOrEmpty(plaidInstitutionId))
                {
                    // Plaid answered: this Item has no directory institution, so there is no colour to find.
                    institution.ColorLookedUpAtUtc = now;
                    return;
                }
                institution.PlaidInstitutionId = plaidInstitutionId;
            }

            var found = await plaid.GetInstitutionAsync(plaidInstitutionId, cancellationToken);
            var colour = found?.PrimaryColorHex;
            institution.PrimaryColorHex = string.IsNullOrWhiteSpace(colour) ? null : colour.Trim();
            institution.ColorLookedUpAtUtc = now;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Rate limit, network, a rejected call: none of them says the bank has no colour, so the
            // marker stays unset and the next refresh asks again.
            logger.LogWarning(ex, "Could not look up the colour for {Name}; the post-it stays default and the lookup is retried next refresh.", institution.Name);
        }
    }
}
