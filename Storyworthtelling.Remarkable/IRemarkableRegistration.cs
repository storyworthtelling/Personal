namespace Storyworthtelling.Remarkable;

// The one-time device-code pairing. The owner visits my.remarkable.com/device/mobile/connect, gets a code,
// and enters it here; the returned long-lived device token is what every later session-token exchange uses.
public interface IRemarkableRegistration
{
    // Exchanges a one-time pairing code for a long-lived device token. Throws HttpRequestException with the
    // status code when the code is rejected.
    Task<string> Register(string oneTimeCode, CancellationToken cancellationToken = default);
}
