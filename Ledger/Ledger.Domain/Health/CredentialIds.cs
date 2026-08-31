namespace Ledger.Domain.Health;

// Stable identifiers for the credentials whose health Ledger tracks. Only the Plaid client id/secret live
// on the Settings page; a specific bank's access-token trouble is a per-institution concern shown in the
// connected-accounts list, not here.
public static class CredentialIds
{
    public const string Plaid = "plaid";
}
