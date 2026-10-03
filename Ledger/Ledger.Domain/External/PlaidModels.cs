namespace Index.Domain.External;

// The plain results the Plaid client hands back to the pipeline. Domain-owned records so the Domain
// stays free of HTTP/JSON concerns; the Infrastructure client maps Plaid's wire format onto these.

// The token pair from /link/token/create. HostedLinkUrl is the Plaid Hosted Link URL the app opens
// in a WebView so the user can connect an institution without a native Link SDK.
public sealed record PlaidLinkToken(string LinkToken, string? HostedLinkUrl);

// The result of exchanging a Link public_token for a durable access_token (/item/public_token/exchange).
public sealed record PlaidItemAccess(string AccessToken, string ItemId, string? InstitutionName);

// The outcome of a completed Hosted Link session (/link/token/get): the public_token to exchange and
// the institution the user chose. Null is returned while the session is still in progress.
// InstitutionId is what a later GetInstitutionAsync call needs (D127) — Plaid's link_sessions payload
// carries it alongside the name, so capturing it here costs nothing extra and lets Release C's connect
// flow fetch the bank's primary_color right after linking, without a second round trip to work out
// which institution it just connected.
public sealed record PlaidLinkResult(string PublicToken, string? InstitutionName, string? InstitutionId = null);

// Plaid's institution directory entry (/institutions/get_by_id). PrimaryColorHex is optional metadata
// ("#rrggbb" when present) — Plaid omits it unless the request sets options.include_optional_metadata.
public sealed record PlaidInstitution(string InstitutionId, string Name, string? PrimaryColorHex);

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
    decimal? Current,
    decimal? Available,
    decimal? Limit);

// One account's liabilities detail from /liabilities/get (credit cards and mortgages), keyed back to
// the account by AccountId. Any field may be null when Plaid doesn't report it for that account.
public sealed record PlaidLiability(
    string AccountId,
    decimal? LastStatementBalance,
    DateTimeOffset? NextPaymentDueDate,
    decimal? NextPaymentAmount,
    decimal? Apr);

// One posted or pending transaction from /transactions/get. Amount follows Plaid's sign convention:
// positive = money leaving the account (a charge/withdrawal), negative = money in (a payment/refund).
//
// PendingTransactionId is Plaid's pointer from a POSTED transaction back to the pending one it replaces:
// Plaid issues a brand-new transaction_id when a pending transaction posts, so the id alone cannot tell
// "the same purchase, settled" from "a second purchase" (R-G18). Null on a pending row and on a posted
// row that never had a pending predecessor.
//
// CategoryPrimary / CategoryDetailed are Plaid's personal_finance_category (R8), e.g. "FOOD_AND_DRINK" /
// "FOOD_AND_DRINK_GROCERIES". Null when Plaid sent none (it is only returned when the request asks for it).
public sealed record PlaidTransaction(
    string TransactionId,
    string AccountId,
    DateTimeOffset Date,
    string Name,
    string? MerchantName,
    double Amount,
    string? IsoCurrencyCode,
    bool Pending,
    string? PendingTransactionId = null,
    string? CategoryPrimary = null,
    string? CategoryDetailed = null);
