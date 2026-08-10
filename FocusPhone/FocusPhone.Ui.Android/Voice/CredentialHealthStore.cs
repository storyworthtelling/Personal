using global::Android.App;
using global::Android.Content;
using Storyworthtelling.Voice.Health;

namespace FocusPhone.Ui.Voice;

// The device-side credential health store for a FocusPhone head, kept in the head's OWN app-private
// SharedPreferences (each head is a separate package). A head's background work reports Ok/Failing through
// the ICredentialHealthSink seam after an authenticated call, and the head's settings screen reads the
// report back to hide a working key's field. Persisted as one JSON blob; the sink stamps the time here.
public sealed class CredentialHealthStore : ICredentialHealthSink
{
    private const string PrefsName = "credentialHealth";
    private const string ReportKey = "report";

    private static ISharedPreferences Prefs() =>
        Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

    public CredentialHealthReport Load() =>
        CredentialHealthCodec.Deserialize(Prefs().GetString(ReportKey, string.Empty));

    public void MarkOk(string credentialId) =>
        Save(Load().MarkOk(credentialId, DateTimeOffset.UtcNow));

    public void MarkFailing(string credentialId, string message) =>
        Save(Load().MarkFailing(credentialId, message, DateTimeOffset.UtcNow));

    private static void Save(CredentialHealthReport report)
    {
        var editor = Prefs().Edit()!;
        editor.PutString(ReportKey, CredentialHealthCodec.Serialize(report));
        editor.Apply();
    }
}
