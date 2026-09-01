namespace Storyworthtelling.Remarkable;

// The long-lived device token from the one-time device-code pairing. Exchanged per session for a short-lived
// user JWT (RemarkableAuthService). Read live from each app's settings so a re-pair takes effect without a
// restart, and backed up to the shared secrets vault so a factory reset restores it without re-pairing.
public class RemarkableOptions
{
    public const string SectionName = "Remarkable";
    public string DeviceToken { get; set; } = string.Empty;
}
