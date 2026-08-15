using Ledger.Domain.Pipeline;

namespace Ledger.Domain.External;

// Access to the Plaid aggregator API. The connect flow (link token → hosted Link → token exchange)
// yields a durable access_token per institution; the refresh flow reads balances and liabilities for
// that token. Implementations raise RateLimitException (Ledger.Domain.Pipeline) on Plaid throttling so
// the worker reschedules instead of failing hard.
public interface IPlaidClient
{
    // Creates a Link token and (via Hosted Link) a URL the app opens in a WebView to connect an
    // institution. This is a hosted (WebView) flow, not the native Android SDK, so no android_package_name
    // is sent — Plaid handles OAuth redirects within the hosted page.
    Task<PlaidLinkToken> CreateLinkTokenAsync(CancellationToken cancellationToken);

    // Reads a Hosted Link session's result (/link/token/get). Returns the public_token + chosen
    // institution once the user has finished connecting, or null while the session is still open.
    Task<PlaidLinkResult?> GetLinkResultAsync(string linkToken, CancellationToken cancellationToken);

    // Polls /link/token/get until the Hosted Link session yields a public_token or the attempt budget
    // is spent. Plaid populates item_add_results a short, variable time AFTER the OAuth redirect returns
    // — pronounced for app-to-app OAuth banks like Chase, where a single read the instant the owner
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
}
