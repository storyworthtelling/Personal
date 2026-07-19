namespace Tally.Domain.External;

// Access to the Plaid aggregator API. The connect flow (link token → hosted Link → token exchange)
// yields a durable access_token per institution; the refresh flow reads balances and liabilities for
// that token. Implementations raise RateLimitException (Tally.Domain.Pipeline) on Plaid throttling so
// the worker reschedules instead of failing hard.
public interface IPlaidClient
{
    // Creates a Link token and (via Hosted Link) a URL the app opens in a WebView to connect an
    // institution. androidPackageName is registered with Plaid so the redirect returns to the app.
    Task<PlaidLinkToken> CreateLinkTokenAsync(string androidPackageName, CancellationToken cancellationToken);

    // Reads a Hosted Link session's result (/link/token/get). Returns the public_token + chosen
    // institution once the user has finished connecting, or null while the session is still open.
    Task<PlaidLinkResult?> GetLinkResultAsync(string linkToken, CancellationToken cancellationToken);

    // Exchanges the public_token produced when the user finishes Link for the durable access_token.
    Task<PlaidItemAccess> ExchangePublicTokenAsync(string publicToken, CancellationToken cancellationToken);

    // Current balances for every account on the Item behind accessToken.
    Task<IReadOnlyList<PlaidAccount>> GetBalancesAsync(string accessToken, CancellationToken cancellationToken);

    // Liabilities detail (credit cards + mortgages) for the Item. Returns an empty list when the
    // institution supports no Liabilities accounts.
    Task<IReadOnlyList<PlaidLiability>> GetLiabilitiesAsync(string accessToken, CancellationToken cancellationToken);

    // Releases the Item at Plaid when the user disconnects an institution, invalidating the access
    // token and stopping any server-side subscription (e.g. Liabilities) billing/consent.
    Task RemoveItemAsync(string accessToken, CancellationToken cancellationToken);
}
