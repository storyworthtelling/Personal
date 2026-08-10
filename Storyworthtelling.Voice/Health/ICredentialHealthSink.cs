namespace Storyworthtelling.Voice.Health;

// The write side of credential health, implemented per app by a device store (Preferences-backed) and
// injected into the API clients/workers. A client marks its credential Ok right after a call authenticated
// with it succeeds, and Failing when a call rejects it (a bad key / expired token). Transient trouble —
// network drops, 5xx, rate limits — must NOT be reported here: those aren't the credential's fault, and
// flagging them would wrongly reveal a settings field for a key that's actually fine. Implementations
// stamp the time themselves, so the interface stays trivial to call from anywhere.
public interface ICredentialHealthSink
{
    void MarkOk(string credentialId);
    void MarkFailing(string credentialId, string message);
}
