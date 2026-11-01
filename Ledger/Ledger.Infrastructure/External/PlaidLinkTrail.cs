using Storyworthtelling.Diagnostics;

namespace Index.Infrastructure.External;

/// <summary>
/// Plaid Hosted Link as a sign-in trail (LOG-LOOP-PLAN.md §4.2): the link token made, Chrome opened, Index coming
/// back, the poll of <c>/link/token/get</c> for the session's public_token, and the exchange. Every step that can end
/// it is here, so a bank that "did nothing" reads as where it stopped. No token of any kind has a parameter here:
/// the link token, the public token and the access token stay out of the log (A3).
/// </summary>
public static class PlaidLinkTrail
{
    public static SignInTrail Start(DateTimeOffset at) =>
        SignInTrail.Start(SignInFlowKinds.PlaidHostedLink, SignInTrail.NewFlowId(), at, null, null, [], SignInBrowserPaths.ChromeByPackage);

    public static SignInTrail LinkTokenFailed(this SignInTrail trail, DateTimeOffset at, string reason) =>
        trail.ProviderError(at, "link_token", reason);

    public static SignInTrail Opened(this SignInTrail trail, DateTimeOffset at) => trail.LeftApp(at);

    public static SignInTrail ChromeFailed(this SignInTrail trail, DateTimeOffset at, string reason) => trail.NoBrowser(at, reason);

    /// <summary>Index is in front again and about to poll; Hosted Link has no redirect into the app, so there is no URL.</summary>
    public static SignInTrail CameBack(this SignInTrail trail, DateTimeOffset at) =>
        trail.Returned(at, "(Index came back to the front; polling /link/token/get)");

    public static SignInTrail NoPublicToken(this SignInTrail trail, DateTimeOffset at, int polls) =>
        trail.TimedOut(at, $"Plaid returned but /link/token/get had no public_token after {polls} polls");

    public static SignInTrail Connected(this SignInTrail trail, DateTimeOffset at) =>
        trail.TokenReceived(at, ["plaid item"], null, "access token (exchanged, stored on the phone)");
}
