namespace Index.Domain.Health;

// Stable identifiers for the credentials whose health Index tracks. Only the Plaid client id/secret live
// on the Settings page; a specific bank's access-token trouble is a per-institution concern shown in the
// connected-accounts list, not here.
public static class CredentialIds
{
    public const string Plaid = "plaid";

    // The shared Claude key behind the owner's-voice greeting (Storyworthtelling.Voice). Index doesn't
    // own or fix this key — it's entered once in Folio (D80) — so nothing in Index ever calls
    // ICredentialHealthSink.MarkOk/MarkFailing for it; a CredentialHealthReport lookup against this id
    // will always read Unknown. It exists as a stable id for the Settings row and the diagnostics export
    // to reference, which check presence in the shared Drive config directly instead.
    public const string Claude = "claude";
}
