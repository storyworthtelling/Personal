using Storyworthtelling.Voice.Health;

namespace DailyLoader.Android;

// The device-side credential health store: the NYT/reMarkable/Anthropic clients report Ok/Failing through
// the ICredentialHealthSink seam (they run on the background delivery worker), and the Settings page reads
// the report back to decide whether a key field needs to be shown. Persisted as one JSON blob in MAUI
// Preferences so it survives across worker runs and app launches; the sink stamps the time here.
public sealed class CredentialHealthStore : ICredentialHealthSink
{
    private const string Key = "CredentialHealthReport";

    public CredentialHealthReport Load() =>
        CredentialHealthCodec.Deserialize(Preferences.Get(Key, string.Empty));

    public void MarkOk(string credentialId) =>
        Save(Load().MarkOk(credentialId, DateTimeOffset.UtcNow));

    public void MarkFailing(string credentialId, string message) =>
        Save(Load().MarkFailing(credentialId, message, DateTimeOffset.UtcNow));

    private static void Save(CredentialHealthReport report) =>
        Preferences.Set(Key, CredentialHealthCodec.Serialize(report));
}
