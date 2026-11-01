using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Maui.Diagnostics;

/// <summary>
/// The reMarkable pairing as a trail (LOG-LOOP-PLAN.md §4.2): the one-time code sent, and either a device token
/// back (its kind only, never the token) or why not — the HTTP status of a refused code, or the failure's type.
/// </summary>
public static class PairingTrail
{
    public static SignInTrail Start() =>
        SignInTrail.Start(SignInFlowKinds.RemarkablePairing, SignInTrail.NewFlowId(), DateTimeOffset.Now, null, null, [], SignInBrowserPaths.None);

    public static void Paired(SignInTrail trail) =>
        MauiLogLoop.SignIn(trail.TokenReceived(DateTimeOffset.Now, ["device"], null, "device token"));

    public static void Failed(SignInTrail trail, Exception e) =>
        MauiLogLoop.SignIn(e is HttpRequestException { StatusCode: { } status }
            ? trail.ExchangeFailed(DateTimeOffset.Now, (int)status, e.GetType().Name)
            : trail.ProviderError(DateTimeOffset.Now, e.GetType().Name, e.Message));
}
