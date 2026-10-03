namespace Index.Domain.Health;

// Classifies a Plaid error_code as a credentials problem (the client id/secret entered on the Settings
// page are wrong) versus anything else — a per-item login/token problem, a rate limit, a transient error.
// Only a credentials rejection should flag the Plaid key field; an item needing re-login is surfaced against
// that bank in the connected-accounts list instead. Pure and unit-tested so the code list is verifiable.
public static class PlaidFailure
{
    // Plaid returns these when the client_id/secret themselves are rejected (error_type INVALID_API_KEYS /
    // INVALID_INPUT on the auth fields). Item/token codes (ITEM_LOGIN_REQUIRED, INVALID_ACCESS_TOKEN, …)
    // are deliberately absent — those are a single bank's problem, not the shared credentials'.
    private static readonly HashSet<string> CredentialCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INVALID_API_KEYS",
        "INVALID_CLIENT_ID",
        "INVALID_SECRET",
        "INVALID_CREDENTIALS",
        "UNAUTHORIZED"
    };

    public static bool IsCredentialRejection(string? errorCode) =>
        errorCode is not null && CredentialCodes.Contains(errorCode);
}
