using Storyworthtelling.Diagnostics;
using Codex.Ui;
using global::Android.Accounts;
using global::Android.App;
using global::Android.Content;

namespace Score.Google;

// Native "Sign in with Google" via the core Android AccountManager: shows the system picker of
// Google accounts already on the device, then requests Drive consent for the chosen account.
// Picking the account also grants this app visibility to it, so the token provider can mint tokens
// for it afterwards. No Play Services binding is needed.
public static class DeviceAccountSignIn
{
    public const int RequestCode = 9001;

    private static TaskCompletionSource<string>? pendingChoice;

    // The sign-in's trail (LOG-LOOP-PLAN.md §4.2), handed to the head log once it ends. Static because the
    // picker's answer arrives through the activity, not through this call.
    private static SignInTrail? trail;
    private static readonly object TrailGate = new();

    public static async Task<string> SignIn(Activity activity)
    {
        lock (TrailGate)
        {
            trail = SignInTrail.Start(SignInFlowKinds.GoogleDeviceAccount, SignInTrail.NewFlowId(), DateTimeOffset.Now,
                null, null, [DeviceAccountTokenProvider.TokenScope], SignInBrowserPaths.AccountPicker);
        }
        try
        {
            var email = await ChooseAccount(activity);
            Step(t => t.ConsentShown(DateTimeOffset.Now));
            await EnsureConsent(activity, email);
            Finish(t => t.TokenReceived(DateTimeOffset.Now, [DeviceAccountTokenProvider.TokenScope], null, "oauth2 (AccountManager)"));
            MusicSettingsStore.GoogleAccountEmail = email;
            return email;
        }
        catch (global::Android.Accounts.OperationCanceledException e)
        {
            Finish(t => t.ConsentDenied(DateTimeOffset.Now, e.GetType().Name));
            throw;
        }
        catch (Exception e) when (e is AuthenticatorException or Java.IO.IOException)
        {
            Finish(t => t.ProviderError(DateTimeOffset.Now, e.GetType().Name, e.Message));
            throw;
        }
    }

    /// <summary>Call from the screen's resume: a sign-in that left the app and heard nothing back is a NoReturn.</summary>
    public static void CheckPending(DateTimeOffset now) => Finish(t => t.CheckNoReturn(now, resumed: true), onlyIfFinished: true);

    private static void Step(Func<SignInTrail, SignInTrail> step)
    {
        lock (TrailGate)
        {
            if (trail is not null && !trail.IsFinished)
            {
                trail = step(trail);
            }
        }
    }

    private static void Finish(Func<SignInTrail, SignInTrail> step, bool onlyIfFinished = false)
    {
        SignInTrail? finished = null;
        lock (TrailGate)
        {
            if (trail is null || trail.IsFinished)
            {
                return;
            }
            var next = step(trail);
            if (onlyIfFinished && !next.IsFinished)
            {
                return;
            }
            trail = null;
            finished = next;
        }
        HeadDiagnostics.Current?.SignIn(finished);
    }

    private static Task<string> ChooseAccount(Activity activity)
    {
        // Positional args (binding parameter names differ from the Java docs):
        // (selectedAccount, allowableAccounts, allowableAccountTypes, descriptionOverrideText,
        //  addAccountAuthTokenType, addAccountRequiredFeatures, addAccountOptions)
        var intent = AccountManager.NewChooseAccountIntent(
            null, null, new[] { "com.google" }, null, null, null, null);

        pendingChoice = new TaskCompletionSource<string>();
        activity.StartActivityForResult(intent, RequestCode);
        Step(t => t.LeftApp(DateTimeOffset.Now));
        return pendingChoice.Task;
    }

    // Invoked from ScoreActivity.OnActivityResult once the picker returns.
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
            Finish(t => t.ProviderError(DateTimeOffset.Now, "no account", "the picker returned no account name"));
            tcs.TrySetException(new InvalidOperationException("No account was returned from the picker."));
        }
        else
        {
            Step(t => t.AccountChosen(DateTimeOffset.Now));
            tcs.TrySetResult(email);
        }
    }

    // Fetches a token interactively (passing the activity) so the Drive consent screen is shown now,
    // rather than failing silently in the background later.
    private static Task EnsureConsent(Activity activity, string email)
    {
        var manager = AccountManager.Get(activity)!;
        var account = new Account(email, "com.google");
        var tcs = new TaskCompletionSource<bool>();
        manager.GetAuthToken(account, DeviceAccountTokenProvider.TokenScope, null, activity,
            new ConsentCallback(tcs), null);
        return tcs.Task;
    }

    private sealed class ConsentCallback(TaskCompletionSource<bool> tcs)
        : Java.Lang.Object, IAccountManagerCallback
    {
        public void Run(IAccountManagerFuture? future)
        {
            try
            {
                _ = future?.GetResult(60_000, Java.Util.Concurrent.TimeUnit.Milliseconds);
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }
    }
}
