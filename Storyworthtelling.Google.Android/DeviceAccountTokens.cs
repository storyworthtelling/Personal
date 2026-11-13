using global::Android.Accounts;
using global::Android.OS;

namespace Storyworthtelling.Google.Android;

/// <summary>
/// Google access tokens for the device account chosen at sign-in, through the core Android AccountManager (no Play
/// Services binding, no stored secret). Four heads and the native voice account each kept a copy differing only in
/// the scopes and the wording (audit 01 §10). The cache rule is <see cref="CachedAccessToken"/>.
///
/// <para>A sign-in Google turns away is said, not posted (audit 11 §4). The background fetch used to ask Android to post
/// Google's "sign in again" notification, which the locked phone's disabled status bar hides, and threw a plain error
/// into a log. It now asks for no notification, throws <see cref="GoogleSignInRequiredException"/> carrying Google's
/// consent intent, and tells <paramref name="signInHealth"/> — null when a token came back, a reason when it did not —
/// so the app's Settings row and dog-ear can say so. A network failure is not a refused sign-in and reports nothing.</para>
/// </summary>
/// <param name="accountEmail">The signed-in account, read on every call.</param>
/// <param name="tokenType">An <c>oauth2:</c> token type, from <see cref="GoogleScopes.ToTokenType"/>.</param>
/// <param name="notSignedIn">What to say when nobody has signed in yet.</param>
/// <param name="signInHealth">Told null when Google gave a token and a reason when it refused the sign-in.</param>
public sealed class DeviceAccountTokens
{
    private const string GoogleAccountType = "com.google";

    // Every token source in the process, so Drive's 401 watch can forget a rejected token without each of the
    // seventeen Drive call sites carrying a way back to the source that issued it (audit 11 §4).
    private static readonly List<WeakReference<DeviceAccountTokens>> Sources = [];
    private static readonly Lock SourcesGate = new();

    private readonly CachedAccessToken cache = new();

    private readonly Func<string?> accountEmail;
    private readonly string notSignedIn;
    private readonly Action<string?>? signInHealth;

    public DeviceAccountTokens(
        Func<string?> accountEmail, string tokenType, string notSignedIn = "Sign in with Google first.", Action<string?>? signInHealth = null)
    {
        this.accountEmail = accountEmail;
        TokenType = tokenType;
        this.notSignedIn = notSignedIn;
        this.signInHealth = signInHealth;
        lock (SourcesGate)
        {
            Sources.RemoveAll(source => !source.TryGetTarget(out _));
            Sources.Add(new WeakReference<DeviceAccountTokens>(this));
        }
    }

    /// <summary>Forgets <paramref name="rejected"/> in whichever token source handed it out.</summary>
    public static void ForgetEverywhere(string rejected)
    {
        List<DeviceAccountTokens> live = [];
        lock (SourcesGate)
        {
            foreach (var source in Sources)
            {
                if (source.TryGetTarget(out var tokens))
                {
                    live.Add(tokens);
                }
            }
        }
        foreach (var tokens in live)
        {
            tokens.Forget(rejected);
        }
    }

    public string TokenType { get; }

    public Task<string> GetAccessToken()
    {
        var email = accountEmail();
        if (string.IsNullOrEmpty(email))
        {
            throw new InvalidOperationException(notSignedIn);
        }

        return cache.GetAsync(stale =>
        {
            var manager = AccountManager.Get(global::Android.App.Application.Context)!;
            if (stale is not null)
            {
                // Drop the cached token first so a stale one is never handed out again.
                manager.InvalidateAuthToken(GoogleAccountType, stale);
            }

            return Fetch(manager, new Account(email, GoogleAccountType));
        });
    }

    /// <summary>
    /// Forgets a token Google rejected (a 401 from Drive), here and in AccountManager, so the next call fetches a fresh
    /// one rather than handing out the rejected one for the rest of its 45 minutes (audit 11 §4).
    /// </summary>
    public void Forget(string rejected)
    {
        if (!cache.Forget(rejected))
        {
            return;
        }
        AccountManager.Get(global::Android.App.Application.Context)!.InvalidateAuthToken(GoogleAccountType, rejected);
    }

    private string Fetch(AccountManager manager, Account account)
    {
        Bundle? bundle;
        try
        {
            // notifyAuthFailure false: the notification Google would post is hidden on the locked phone, so the refusal is
            // reported here instead and the foreground "Sign in again" asks for consent.
            var future = manager.GetAuthToken(account, TokenType, null, false, null, null);
            var raw = future?.GetResult(60_000, Java.Util.Concurrent.TimeUnit.Milliseconds);
            bundle = raw is null ? null : global::Android.Runtime.Extensions.JavaCast<Bundle>(raw);
        }
        catch (Exception e) when (e is AuthenticatorException or global::Android.Accounts.OperationCanceledException)
        {
            signInHealth?.Invoke("Google refused the sign-in.");
            throw new GoogleSignInRequiredException("Google refused this phone's sign-in. Sign in with Google again.", null, e);
        }

        if (bundle?.GetString(AccountManager.KeyAuthtoken) is { Length: > 0 } token)
        {
            signInHealth?.Invoke(null);
            return token;
        }

        var consent = bundle?.GetParcelable(AccountManager.KeyIntent);
        signInHealth?.Invoke("Google wants the sign-in confirmed again.");
        throw new GoogleSignInRequiredException("Google wants this phone's sign-in confirmed again. Sign in with Google again.", consent);
    }
}
