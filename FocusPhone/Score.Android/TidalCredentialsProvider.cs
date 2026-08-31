using Com.Tidal.Sdk.Auth;
using Com.Tidal.Sdk.Auth.Model;
using Kotlin.Coroutines;
using Codex.Infrastructure.Music;

namespace Score;

/// <summary>
/// Supplies the TIDAL Player SDK with a valid access token on demand — TIDAL's own
/// <c>CredentialsProvider</c>, a Kotlin suspend interface.
///
/// The SDK calls <b>into</b> this class and hands it a <see cref="IContinuation"/> to
/// complete once the (async) token fetch is done, rather than this class calling
/// <b>into</b> a suspend function itself. That is the tractable direction of the
/// coroutine bridge: this class owns the <see cref="IContinuation"/> and can complete
/// it whenever the async work behind <paramref name="refreshAccessToken"/> finishes.
/// Calling into TIDAL's own suspend sign-in methods was the opposite, harder
/// direction, which is why device-code sign-in went through plain HTTP instead of the
/// binding (see <c>TidalDeviceAuthClient</c>).
///
/// <see cref="GetCredentials"/> still has to satisfy Kotlin's suspend-function JVM
/// ABI: return <see cref="TidalKotlinBridge.CoroutineSuspended"/> to signal "the
/// answer comes later", then eventually call <c>IContinuation.ResumeWith</c> with the
/// real <see cref="AuthResult"/>. Every step is logged — this is unverified except by
/// running it on a device, so any wrong assumption about that ABI needs to show up in
/// Logcat as a specific failed step, not as a silent hang.
/// </summary>
public sealed class TidalCredentialsProvider(Func<Task<TidalTokenPollResult>> refreshAccessToken)
    : Java.Lang.Object, ICredentialsProvider
{
    private const string LogTag = "TidalCredentialsProvider";
    private const string ClientId = "";

    /// <summary>
    /// Push-based credential-change notifications — nothing in Score refreshes
    /// credentials outside of <see cref="GetCredentials"/> being called, so this
    /// answers with an empty, never-emitting Flow rather than pretending to support a
    /// feature nothing here uses.
    /// </summary>
    public Xamarin.KotlinX.Coroutines.Flow.IFlow Bus
    {
        get
        {
            var flow = TidalKotlinBridge.EmptyFlow();
            if (flow is null)
            {
                // Nothing sane to return — every caller of a Kotlin Flow property
                // expects a real Flow, never null. Logged loudly; if the SDK reads
                // this at all, the crash that follows is the truest possible signal
                // that the raw JNI lookup needs fixing, not a caught-and-hidden one.
                Android.Util.Log.Error(LogTag, "EmptyFlow() bridge call failed; Bus has nothing valid to return.");
            }

            return flow!;
        }
    }

    public bool IsUserLoggedIn { get; private set; }

    public Java.Lang.Object? GetCredentials(string? apiErrorSubStatus, IContinuation p1)
    {
        Android.Util.Log.Debug(LogTag, $"GetCredentials(apiErrorSubStatus=\"{apiErrorSubStatus}\") called");

        _ = Task.Run(async () =>
        {
            AuthResult result;
            try
            {
                var poll = await refreshAccessToken();
                Android.Util.Log.Debug(LogTag, $"refreshAccessToken completed: status={poll.Status}");
                result = poll.Status == TidalTokenPollStatus.Success
                    ? BuildSuccess(poll)
                    : BuildFailure(poll.Status.ToString(), apiErrorSubStatus);
                IsUserLoggedIn = poll.Status == TidalTokenPollStatus.Success;
            }
            catch (Exception e)
            {
                Android.Util.Log.Error(LogTag, $"refreshAccessToken threw: {e}");
                result = BuildFailure("exception", apiErrorSubStatus);
            }

            try
            {
                Android.Util.Log.Debug(LogTag, $"Resuming continuation with {result.GetType().Name}");
                p1.ResumeWith(result);
            }
            catch (Exception e)
            {
                // If this throws, the coroutine that is awaiting p1 hangs forever —
                // there is nothing further this method can do about it, but it must
                // not vanish silently.
                Android.Util.Log.Error(LogTag, $"ResumeWith itself threw — the caller's coroutine will hang: {e}");
            }
        });

        var sentinel = TidalKotlinBridge.CoroutineSuspended();
        if (sentinel is null)
        {
            // The async ResumeWith above still fires eventually, but returning null
            // here instead of the real sentinel is unverified against what the SDK's
            // generated caller code actually checks for — this is exactly the
            // condition device testing needs to catch, logged so it is easy to find.
            Android.Util.Log.Error(LogTag,
                "CoroutineSuspended() bridge call failed; returning null instead of the real sentinel " +
                "(unverified whether the SDK's caller handles that the same way).");
        }

        return sentinel;
    }

    private static AuthResult BuildSuccess(TidalTokenPollResult poll)
    {
        var credentials = new Credentials(
            clientId: ClientId,
            requestedScopes: [],
            clientUniqueKey: null,
            grantedScopes: [],
            userId: poll.UserId,
            expires: poll.ExpiresIn > 0
                ? Java.Lang.Long.ValueOf(DateTimeOffset.UtcNow.AddSeconds(poll.ExpiresIn).ToUnixTimeMilliseconds())
                : null,
            token: poll.AccessToken);
        return new AuthResult.Success(credentials);
    }

    private static AuthResult BuildFailure(string code, string? apiErrorSubStatus)
    {
        var subStatus = int.TryParse(apiErrorSubStatus, out var parsed) ? Java.Lang.Integer.ValueOf(parsed) : null;
        var error = new Com.Tidal.Sdk.Auth.Model.AuthorizationError(code, subStatus, throwable: null);
        return new AuthResult.Failure(error);
    }
}
