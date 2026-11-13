namespace Storyworthtelling.Voice.Health;

/// <summary>Where a head keeps its credential health report: one JSON text, read and written whole.</summary>
public interface ICredentialHealthStorage
{
    string Read();

    void Write(string report);
}

/// <summary>
/// The one credential health store (audit 01 §14). The four per-head copies did an unlocked load, modify and
/// save while a background worker and parallel clients marked health at once, so one credential's mark could
/// overwrite another's. Every mark here is a read-modify-write under one process-wide lock; a head supplies
/// only its storage (MAUI Preferences or SharedPreferences), and a process has one report.
/// </summary>
public sealed class CredentialHealthStore(ICredentialHealthStorage storage, Func<DateTimeOffset>? clock = null) : ICredentialHealthSink
{
    private static readonly Lock Gate = new();

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);

    public CredentialHealthReport Load()
    {
        lock (Gate)
        {
            return CredentialHealthCodec.Deserialize(storage.Read());
        }
    }

    public void MarkOk(string credentialId) => Update(report => report.MarkOk(credentialId, now()));

    public void MarkFailing(string credentialId, string message) =>
        Update(report => report.MarkFailing(credentialId, message, now()));

    /// <summary>Applies <paramref name="change"/> to the stored report under the store's lock.</summary>
    public void Change(Func<CredentialHealthReport, CredentialHealthReport> change) => Update(change);

    /// <summary>Back to Unknown, for a credential just replaced (see <see cref="CredentialHealthReport.Forget"/>).</summary>
    public void Forget(string credentialId) => Update(report => report.Forget(credentialId));

    private void Update(Func<CredentialHealthReport, CredentialHealthReport> change)
    {
        lock (Gate)
        {
            storage.Write(CredentialHealthCodec.Serialize(change(CredentialHealthCodec.Deserialize(storage.Read()))));
        }
    }
}

/// <summary>
/// The ids of the credentials more than one app holds (audit 01 §15). They are wire names: a stored health
/// report is keyed by them. App-only ids (Plaid, NYT, Gmail, TIDAL, Sleep Number) stay in each app.
/// </summary>
public static class SharedCredentialIds
{
    /// <summary>The one Claude key, in the shared vault.</summary>
    public const string Claude = "claude";

    public const string Gemini = "gemini";

    /// <summary>The reMarkable device token, paired once in any app.</summary>
    public const string Remarkable = "remarkable";

    public const string GitHubUpdateToken = "github-update-token";

    /// <summary>The phone's Google sign-in, which every app's Drive access rides on (audit 11 §4).</summary>
    public const string GoogleAccount = "google-account";
}
