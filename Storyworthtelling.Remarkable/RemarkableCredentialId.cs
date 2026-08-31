namespace Storyworthtelling.Remarkable;

// The stable credential-health id for the reMarkable device token, shared across every app so the client
// that reports the health (RemarkableAuthService) and the settings screens that read it never drift on a
// string literal. Reported failing on a 401/403 exchanging the device token, Ok on a successful exchange.
public static class RemarkableCredentialId
{
    public const string Value = "remarkable";
}
