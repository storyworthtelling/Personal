using global::Android.Accounts;
using global::Android.App;
using global::Android.Content;
using Storyworthtelling.Diagnostics;

namespace Codex.Ui.Voice;

// Device-account Google sign-in + access token for the shared owner's-voice profile AND the encrypted
// secrets vault on Drive, for heads that don't already have their own Google account (Audio does, for
// streaming music). Full `drive` scope: reading the voice profile only needs read, but writing this head's
// section of the secrets vault needs write, so both go through one sign-in. The chosen account email is
// stored in the head's OWN SharedPreferences (a fixed key, but each app's prefs are isolated), so every head
// keeps its own selection. Changing the scope changes the consent screen — re-register + re-consent.
public sealed class VoiceGoogleAccount
{
    public const int RequestCode = 9201;
    private const string DriveScope = "https://www.googleapis.com/auth/drive";
    private const string PrefsName = "voice";
    private const string EmailKey = "googleAccountEmail";

    private readonly string tokenScope;

    /// <summary>
    /// Drive alone, which is what every head needs for the voice profile and the vault.
    /// </summary>
    public VoiceGoogleAccount() : this([])
    {
    }

    /// <summary>
    /// Drive plus whatever else this head needs — Post adds the two Gmail scopes.
    /// The scope string IS the AccountManager token type, so a head asking for more gets
    /// its own token rather than sharing one; and because it is also the consent screen,
    /// adding a scope means re-registering the OAuth client and consenting again.
    /// </summary>
    public VoiceGoogleAccount(params string[] extraScopes)
    {
        var scopes = new[] { DriveScope }
            .Concat(extraScopes.Where(scope => !string.IsNullOrWhiteSpace(scope)))
            .Distinct()
            .OrderBy(scope => scope, StringComparer.Ordinal);
        tokenScope = "oauth2:" + string.Join(' ', scopes);
    }

    private static TaskCompletionSource<string>? pendingChoice;
    private readonly object gate = new();
    private string? cachedToken;
    private DateTimeOffset expiresAt;

    private static ISharedPreferences Prefs() =>
        global::Android.App.Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

    public static string Email => Prefs().GetString(EmailKey, string.Empty) ?? string.Empty;

    public static bool IsSignedIn => !string.IsNullOrEmpty(Email);

    private static void SetEmail(string value)
    {
        var editor = Prefs().Edit()!;
        editor.PutString(EmailKey, value);
        editor.Apply();
    }

    public Task<string> GetAccessToken()
    {
        lock (gate)
        {
            if (cachedToken is not null && DateTimeOffset.UtcNow < expiresAt)
                return Task.FromResult(cachedToken);
        }

        var email = Email;
        if (string.IsNullOrEmpty(email))
            throw new InvalidOperationException("Sign in with Google first.");

        return Task.Run(() =>
        {
            var manager = AccountManager.Get(global::Android.App.Application.Context)!;
            var account = new Account(email, "com.google");
            lock (gate)
            {
                if (cachedToken is not null)
                    manager.InvalidateAuthToken("com.google", cachedToken);
            }

            var token = manager.BlockingGetAuthToken(account, tokenScope, true)
                ?? throw new InvalidOperationException("Could not get a Google token. Sign in again.");
            lock (gate)
            {
                cachedToken = token;
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(45);
            }
            return token;
        });
    }

    // The sign-in under way and where its trail goes once it ends (LOG-LOOP-PLAN.md §4.2). Static, like
    // pendingChoice, because the picker's answer arrives through the activity, not through this instance.
    private static SignInTrail? pendingTrail;
    private static Action<SignInTrail>? pendingTrailSink;
    private static readonly object TrailGate = new();

    /// <summary>Signs in, trailed into the head's log as a device-account sign-in (LOG-LOOP-PLAN.md §4.2).</summary>
    public Task<string> SignIn(Activity activity) => SignIn(activity, SignInFlowKinds.GoogleDeviceAccount, ToHeadLog);

    private static void ToHeadLog(SignInTrail trail) => HeadDiagnostics.Current?.SignIn(trail);

    /// <summary>
    /// Signs in, and when <paramref name="onTrail"/> is given, records the sign-in as a
    /// <see cref="SignInTrail"/> under <paramref name="flowKind"/> and hands it over once it has ended:
    /// picker chosen or cancelled, consent shown, declined or needing the user, a token (its scopes and
    /// type, never the token) — or nothing at all, which <see cref="CheckPendingSignIn"/> turns into
    /// NoReturn on resume or after ten minutes.
    /// </summary>
    public async Task<string> SignIn(Activity activity, string? flowKind, Action<SignInTrail>? onTrail)
    {
        var scopes = tokenScope["oauth2:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (onTrail is not null)
        {
            lock (TrailGate)
            {
                pendingTrail = SignInTrail.Start(
                    flowKind ?? SignInFlowKinds.GoogleDeviceAccount, SignInTrail.NewFlowId(), DateTimeOffset.Now,
                    clientId: null, redirectUri: null, scopes, SignInBrowserPaths.AccountPicker);
                pendingTrailSink = onTrail;
            }
        }

        try
        {
            var email = await ChooseAccount(activity);
            Step(t => t.ConsentShown(DateTimeOffset.Now));
            var granted = await EnsureConsent(activity, email);
            if (!granted)
            {
                Finish(t => t.RecoverableAuth(DateTimeOffset.Now, "AccountManager returned a consent intent instead of a token"));
                throw new InvalidOperationException("Google asked for consent that was not given.");
            }
            Finish(t => t.TokenReceived(DateTimeOffset.Now, scopes, DateTimeOffset.Now.AddMinutes(45), "oauth2 (AccountManager)"));
            SetEmail(email);
            return email;
        }
        catch (global::Android.Accounts.OperationCanceledException e)
        {
            Finish(t => t.ConsentDenied(DateTimeOffset.Now, e.GetType().Name));
            throw;
        }
        catch (Exception e) when (e is global::Android.Accounts.AuthenticatorException or Java.IO.IOException)
        {
            Finish(t => t.ProviderError(DateTimeOffset.Now, e.GetType().Name, e.Message));
            throw;
        }
    }

    /// <summary>
    /// Call from the screen's resume: a sign-in that left the app and has heard nothing back since is
    /// recorded as NoReturn, as is one that has waited ten minutes.
    /// </summary>
    public static void CheckPendingSignIn(DateTimeOffset now, bool resumed) =>
        Finish(t => t.CheckNoReturn(now, resumed), onlyIfFinished: true);

    private static Task<string> ChooseAccount(Activity activity)
    {
        var intent = AccountManager.NewChooseAccountIntent(
            null, null, new[] { "com.google" }, null, null, null, null);
        pendingChoice = new TaskCompletionSource<string>();
        activity.StartActivityForResult(intent, RequestCode);
        Step(t => t.LeftApp(DateTimeOffset.Now));
        return pendingChoice.Task;
    }

    // Called from the head's Activity.OnActivityResult once the picker returns. The trail is stepped here,
    // synchronously, because the activity's resume follows at once and must already see the answer.
    public static void HandleChooserResult(Result resultCode, Intent? data)
    {
        var tcs = pendingChoice;
        if (tcs is null)
            return;
        pendingChoice = null;

        if (resultCode != Result.Ok || data is null)
        {
            Finish(t => t.AccountCancelled(DateTimeOffset.Now));
            tcs.TrySetException(new InvalidOperationException("Account selection was cancelled."));
            return;
        }

        var email = data.GetStringExtra(AccountManager.KeyAccountName);
        if (string.IsNullOrEmpty(email))
        {
            Finish(t => t.AccountCancelled(DateTimeOffset.Now));
            tcs.TrySetException(new InvalidOperationException("No account was returned from the picker."));
        }
        else
        {
            Step(t => t.AccountChosen(DateTimeOffset.Now));
            tcs.TrySetResult(email);
        }
    }

    private static void Step(Func<SignInTrail, SignInTrail> step)
    {
        lock (TrailGate)
        {
            if (pendingTrail is not null)
            {
                pendingTrail = step(pendingTrail);
            }
        }
    }

    // Applies the step; if the trail has ended (or always, unless onlyIfFinished), hands it to the sink.
    private static void Finish(Func<SignInTrail, SignInTrail> step, bool onlyIfFinished = false)
    {
        SignInTrail? ended = null;
        Action<SignInTrail>? sink = null;
        lock (TrailGate)
        {
            if (pendingTrail is null)
            {
                return;
            }
            pendingTrail = step(pendingTrail);
            if (!onlyIfFinished || pendingTrail.IsFinished)
            {
                ended = pendingTrail;
                sink = pendingTrailSink;
                pendingTrail = null;
                pendingTrailSink = null;
            }
        }
        if (ended is not null)
        {
            try
            {
                sink?.Invoke(ended);
            }
            catch (Exception e)
            {
                global::Android.Util.Log.Warn("VoiceGoogleAccount", $"Could not record a sign-in trail: {e.Message}");
            }
        }
    }

    // True when a token came back; false when AccountManager handed back a consent intent instead.
    private Task<bool> EnsureConsent(Activity activity, string email)
    {
        var manager = AccountManager.Get(activity)!;
        var account = new Account(email, "com.google");
        var tcs = new TaskCompletionSource<bool>();
        manager.GetAuthToken(account, tokenScope, null, activity, new ConsentCallback(tcs), null);
        return tcs.Task;
    }

    private sealed class ConsentCallback(TaskCompletionSource<bool> tcs)
        : Java.Lang.Object, IAccountManagerCallback
    {
        public void Run(IAccountManagerFuture? future)
        {
            try
            {
                var raw = future?.GetResult(60_000, Java.Util.Concurrent.TimeUnit.Milliseconds);
                var result = raw is null ? null : global::Android.Runtime.Extensions.JavaCast<global::Android.OS.Bundle>(raw);
                var hasToken = !string.IsNullOrEmpty(result?.GetString(AccountManager.KeyAuthtoken));
                var needsUser = result?.ContainsKey(AccountManager.KeyIntent) == true;
                tcs.TrySetResult(hasToken || !needsUser);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }
    }
}
