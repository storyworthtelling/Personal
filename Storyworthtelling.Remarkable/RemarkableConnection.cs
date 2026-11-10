using Microsoft.Extensions.Options;
using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Remarkable;

/// <summary>
/// The one way a head builds its tablet client (audit 01 §11): the auth exchange and the storage calls on one
/// HttpClient (give it <see cref="RemarkableLimits.Timeout"/>), the device token read live so a re-pair takes
/// effect at once, and the token's health reported. Constitution read a snapshot, and Plate gave the auth service
/// no health sink, so a rejected pairing there was never shown.
/// </summary>
public static class RemarkableConnection
{
    public static IRemarkableClient Create(
        HttpClient http, IOptions<RemarkableOptions> options, ICredentialHealthSink? health = null) =>
        new RemarkableStorageClient(http, new RemarkableAuthService(http, options, health));
}
