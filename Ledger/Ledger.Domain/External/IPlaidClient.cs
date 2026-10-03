using Index.Domain.Pipeline;

namespace Index.Domain.External;

// Access to the Plaid aggregator API. The connect flow (link token → hosted Link → token exchange)
// yields a durable access_token per institution; the refresh flow reads balances and liabilities for
// that token. Implementations raise RateLimitException (Index.Domain.Pipeline) on Plaid throttling so
// the worker reschedules instead of failing hard.
public interface IPlaidClient
{
    // Creates a Link token and (via Hosted Link) a URL the app opens in a WebView to connect an
    // institution. This is a hosted (WebView) flow, not the native Android SDK, so no android_package_name
    // is sent — Plaid handles OAuth redirects within the hosted page.
    Task<PlaidLinkToken> CreateLinkTokenAsync(CancellationToken cancellationToken);

    // D150: creates a Link token in Plaid's UPDATE MODE against an existing Item, by passing its
    // access_token — for repairing a broken connection, never for a first connect. The Trial plan's
    // ten-Item cap counts Items CREATED, so a "repair" that called the plain CreateLinkTokenAsync above
    // would silently mint a fresh Item and spend one of the household's ten Trial connections on a bank
    // that was never actually lost (Guard G16). Still a Hosted Link (WebView) flow, so no
    // android_package_name, same as the fresh-Link overload.
    Task<PlaidLinkToken> CreateUpdateLinkTokenAsync(string existingAccessToken, CancellationToken cancellationToken);

    // Reads a Hosted Link session's result (/link/token/get). Returns the public_token + chosen
    // institution once the user has finished connecting, or null while the session is still open.
    Task<PlaidLinkResult?> GetLinkResultAsync(string linkToken, CancellationToken cancellationToken);

    // Polls /link/token/get until the Hosted Link session yields a public_token or the attempt budget
    // is spent. Plaid populates item_add_results a short, variable time AFTER the OAuth redirect returns
    // — pronounced for app-to-app OAuth banks, where a single read the instant the owner
    // switches back sees an empty session and the institution is silently never persisted. Retries up to
    // maxAttempts with exponential backoff, returning the result the moment it appears (issuing no further
    // reads), or null if the session never completes in the budget. delay is the wait between attempts —
    // injectable so tests run without real time; null uses Task.Delay.
    Task<PlaidLinkResult?> PollLinkResultAsync(
        string linkToken, int maxAttempts, BackoffScheduler backoff,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken cancellationToken = default);

    // Exchanges the public_token produced when the user finishes Link for the durable access_token.
    Task<PlaidItemAccess> ExchangePublicTokenAsync(string publicToken, CancellationToken cancellationToken);

    // Current balances for every account on the Item behind accessToken.
    Task<IReadOnlyList<PlaidAccount>> GetBalancesAsync(string accessToken, CancellationToken cancellationToken);

    // Liabilities detail (credit cards + mortgages) for the Item. Returns an empty list when the
    // institution supports no Liabilities accounts.
    Task<IReadOnlyList<PlaidLiability>> GetLiabilitiesAsync(string accessToken, CancellationToken cancellationToken);

    // Recent transactions for the given accounts between startDate and endDate (inclusive), most
    // "count" of them. Used on demand when an account row is expanded, so it isn't part of the
    // background refresh. Throws if the Item's Transactions product isn't ready yet.
    Task<IReadOnlyList<PlaidTransaction>> GetTransactionsAsync(
        string accessToken, IReadOnlyList<string> accountIds,
        DateOnly startDate, DateOnly endDate, int count, CancellationToken cancellationToken);

    // Releases the Item at Plaid when the user disconnects an institution, invalidating the access
    // token and stopping any server-side subscription (e.g. Liabilities) billing/consent.
    Task RemoveItemAsync(string accessToken, CancellationToken cancellationToken);

    // Plaid's institution directory lookup (/institutions/get_by_id), keyed by the institution_id Link
    // reports for a connected Item (not an access token — this is metadata about the bank itself, the
    // same for every customer). Fetched once per institution and cached on Institution.PrimaryColorHex
    // (D127) rather than looked up every time a post-it opens. Requires
    // options.include_optional_metadata: true, since primary_color is optional metadata Plaid omits by
    // default.
    Task<PlaidInstitution> GetInstitutionAsync(string institutionId, CancellationToken cancellationToken);

    // Which institution an Item belongs to (/item/get → item.institution_id), for a bank connected before
    // its institution id was stored. Returns null when Plaid reports none for the Item. Free and not rate
    // sensitive in the way balance reads are, but it can still throw; callers treat a throw as "ask again
    // next refresh".
    Task<string?> GetItemInstitutionIdAsync(string accessToken, CancellationToken cancellationToken);
}
