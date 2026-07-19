namespace Ledger.Domain.External;

// The plain results the Plaid client hands back to the pipeline. Domain-owned records so the Domain
// stays free of HTTP/JSON concerns; the Infrastructure client maps Plaid's wire format onto these.

// The token pair from /link/token/create. HostedLinkUrl is the Plaid Hosted Link URL the app opens
// in a WebView so the user can connect an institution without a native Link SDK.
public sealed record PlaidLinkToken(string LinkToken, string? HostedLinkUrl);

// The result of exchanging a Link public_token for a durable access_token (/item/public_token/exchange).
public sealed record PlaidItemAccess(string AccessToken, string ItemId, string? InstitutionName);

// The outcome of a completed Hosted Link session (/link/token/get): the public_token to exchange and
// the institution the user chose. Null is returned while the session is still in progress.
public sealed record PlaidLinkResult(string PublicToken, string? InstitutionName);

// One account's current balances from /accounts/balance/get. Type/Subtype are Plaid's raw strings
// (e.g. "depository"/"checking", "credit"/"credit card", "loan"/"mortgage").
public sealed record PlaidAccount(
    string AccountId,
    string Name,
    string? OfficialName,
    string? Mask,
    string Type,
    string? Subtype,
    string? IsoCurrencyCode,
    double? Current,
    double? Available,
    double? Limit);

// One account's liabilities detail from /liabilities/get (credit cards and mortgages), keyed back to
// the account by AccountId. Any field may be null when Plaid doesn't report it for that account.
public sealed record PlaidLiability(
    string AccountId,
    double? LastStatementBalance,
    DateTimeOffset? NextPaymentDueDate,
    double? NextPaymentAmount,
    double? Apr);

// One posted or pending transaction from /transactions/get. Amount follows Plaid's sign convention:
// positive = money leaving the account (a charge/withdrawal), negative = money in (a payment/refund).
public sealed record PlaidTransaction(
    string TransactionId,
    string AccountId,
    DateTimeOffset Date,
    string Name,
    string? MerchantName,
    double Amount,
    string? IsoCurrencyCode,
    bool Pending);
